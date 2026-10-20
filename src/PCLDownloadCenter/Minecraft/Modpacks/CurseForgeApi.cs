using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>CurseForge 整合包 manifest 中声明的一个文件。</summary>
    internal sealed class CfRequestedFile
    {
        public long ProjectId;
        public long FileId;
        public bool Required = true;
    }

    /// <summary>CurseForge API 返回的文件信息。</summary>
    internal sealed class CfFile
    {
        public long Id;
        public long ModId;
        public string FileName;
        public long Length = -1;
        public string DownloadUrl;
        public string Sha1;
        public string DisplayName;
        /// <summary>文件内部条目名（modules），用于判断放到 mods / resourcepacks / shaderpacks。</summary>
        public List<string> Modules = new List<string>();
    }

    /// <summary>
    /// CurseForge API 最小封装。
    /// 有 CurseForgeApiKey 时优先官方 api.curseforge.com；
    /// 无 Key 时尝试 PCL 同款第三方代理 mod.mcimirror.top（不可用时返回清晰错误）。
    /// </summary>
    internal static class CurseForgeApi
    {
        private const string OfficialBase = "https://api.curseforge.com/v1";
        private const string MirrorBase = "https://mod.mcimirror.top/curseforge/v1";

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch { }
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseProxy = true
            };
            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Downloader.UserAgent);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            return client;
        }

        public static bool HasKey(AppConfig cfg)
        {
            return cfg != null && !string.IsNullOrWhiteSpace(cfg.CurseForgeApiKey);
        }

        // ---------------- HTTP ----------------

        private static async Task<string> SendAsync(InstallContext ctx, HttpMethod method, string url, string jsonBody, bool withKey)
        {
            Exception last = null;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                ctx.Ct.ThrowIfCancellationRequested();
                try
                {
                    using (var request = new HttpRequestMessage(method, url))
                    {
                        if (jsonBody != null)
                            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                        if (withKey && HasKey(ctx.Cfg))
                            request.Headers.TryAddWithoutValidation("x-api-key", ctx.Cfg.CurseForgeApiKey.Trim());
                        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.Ct))
                        {
                            cts.CancelAfter(TimeSpan.FromSeconds(40));
                            using (var resp = await Http.SendAsync(request, cts.Token).ConfigureAwait(false))
                            {
                                var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                                if (!resp.IsSuccessStatusCode)
                                {
                                    var detail = (text ?? "").Trim();
                                    if (detail.StartsWith("<", StringComparison.Ordinal)) detail = ""; // HTML 错误页不进入提示
                                    throw new Exception("HTTP " + (int)resp.StatusCode +
                                                        (detail.Length > 0 ? " " + PackCore.Shorten(detail, 160) : "（服务不可用）"));
                                }
                                return text;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (ctx.Ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    last = ex;
                    await Task.Delay(400 * attempt, ctx.Ct).ConfigureAwait(false);
                }
            }
            throw new Exception("请求失败：" + url + " :: " + (last == null ? "未知错误" : last.Message), last);
        }

        private static string BuildApiError(string path, Exception keyError, Exception proxyError)
        {
            var sb = new StringBuilder();
            sb.Append("CurseForge 接口不可用（").Append(path).Append("）：");
            if (keyError != null) sb.Append("已配置的 API Key 调用失败（").Append(keyError.Message).Append("）；");
            if (proxyError != null) sb.Append("未配置 API Key 或官方接口失败，且第三方镜像 mod.mcimirror.top 不可用（").Append(proxyError.Message).Append("）");
            sb.Append("。请在设置中填写 CurseForge API Key，或改用 Modrinth 整合包。");
            return sb.ToString();
        }

        /// <summary>GET 一个 CurseForge API 路径（先官方 + Key，再第三方代理）。</summary>
        public static async Task<JObject> GetJsonAsync(InstallContext ctx, string path)
        {
            string text = null;
            Exception keyError = null;
            Exception proxyError = null;
            if (HasKey(ctx.Cfg))
            {
                try { text = await SendAsync(ctx, HttpMethod.Get, OfficialBase + path, null, true).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { keyError = ex; }
            }
            if (text == null)
            {
                try { text = await SendAsync(ctx, HttpMethod.Get, MirrorBase + path, null, false).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { proxyError = ex; }
            }
            if (text == null) throw new Exception(BuildApiError(path, keyError, proxyError));
            return JObject.Parse(text);
        }

        // ---------------- 数据 ----------------

        /// <summary>批量查询文件信息（manifest 的 files[] 用）。</summary>
        public static async Task<List<CfFile>> GetFilesByIdsAsync(InstallContext ctx, IList<long> fileIds)
        {
            var result = new List<CfFile>();
            if (fileIds == null || fileIds.Count == 0) return result;
            var body = new JObject { ["fileIds"] = new JArray(fileIds.Cast<object>().ToArray()) }.ToString(Newtonsoft.Json.Formatting.None);
            string text = null;
            Exception keyError = null;
            Exception proxyError = null;
            if (HasKey(ctx.Cfg))
            {
                try { text = await SendAsync(ctx, HttpMethod.Post, OfficialBase + "/mods/files", body, true).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    keyError = ex;
                    ctx.Warn("CurseForge 官方接口调用失败：" + ex.Message);
                }
            }
            if (text == null)
            {
                try { text = await SendAsync(ctx, HttpMethod.Post, MirrorBase + "/mods/files", body, false).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { proxyError = ex; }
            }
            if (text == null)
            {
                throw new Exception("CurseForge 整合包需要从 CurseForge 下载 " + fileIds.Count + " 个文件，但下载接口不可用。" +
                                    "请在设置中填写 CurseForge API Key，或改用 Modrinth 整合包。" +
                                    "（" + (keyError != null ? "API Key 请求失败：" + keyError.Message : "镜像 mod.mcimirror.top 不可用：" + (proxyError == null ? "未知原因" : proxyError.Message)) + "）");
            }
            var data = JObject.Parse(text)["data"] as JArray;
            if (data == null) throw new Exception("CurseForge 返回的数据格式异常（缺少 data 数组）");
            foreach (var token in data)
            {
                var file = ParseFile(token as JObject);
                if (file != null) result.Add(file);
            }
            return result;
        }

        /// <summary>获取项目信息（名称、图标）。</summary>
        public static async Task<JObject> GetModAsync(InstallContext ctx, long modId)
        {
            var obj = await GetJsonAsync(ctx, "/mods/" + modId.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            return obj["data"] as JObject;
        }

        /// <summary>获取项目文件列表（按日期从新到旧）。</summary>
        public static async Task<List<CfFile>> GetModFilesAsync(InstallContext ctx, long modId, int pageSize = 50)
        {
            var obj = await GetJsonAsync(ctx, "/mods/" + modId.ToString(CultureInfo.InvariantCulture) +
                                              "/files?pageSize=" + pageSize + "&index=0").ConfigureAwait(false);
            var data = obj["data"] as JArray;
            var list = new List<CfFile>();
            if (data == null) return list;
            foreach (var token in data)
            {
                var file = ParseFile(token as JObject);
                if (file != null) list.Add(file);
            }
            list.Sort((a, b) => b.Id.CompareTo(a.Id));
            return list;
        }

        private static CfFile ParseFile(JObject obj)
        {
            if (obj == null) return null;
            var file = new CfFile
            {
                Id = JsonUtil.Long(obj, "id", -1),
                ModId = JsonUtil.Long(obj, "modId", -1),
                FileName = JsonUtil.Str(obj, "fileName", null),
                Length = JsonUtil.Long(obj, "fileLength", -1),
                DownloadUrl = JsonUtil.Str(obj, "downloadUrl", null),
                DisplayName = JsonUtil.Str(obj, "displayName", null)
            };
            var hashes = obj["hashes"] as JArray;
            if (hashes != null)
            {
                foreach (var h in hashes)
                {
                    var hash = h as JObject;
                    if (hash == null) continue;
                    var algo = JsonUtil.Long(hash, "algo", -1);
                    if (algo == 1) file.Sha1 = JsonUtil.Str(hash, "value", null);
                }
            }
            var modules = obj["modules"] as JArray;
            if (modules != null)
            {
                foreach (var token in modules)
                {
                    var module = token as JObject;
                    if (module == null) continue;
                    var name = JsonUtil.Str(module, "name", null);
                    if (!string.IsNullOrWhiteSpace(name)) file.Modules.Add(name);
                }
            }
            return file;
        }

        // ---------------- 下载地址 ----------------

        /// <summary>把 forgecdn 地址改写成 mod.mcimirror.top 镜像地址（PCL 同款）。</summary>
        public static List<string> MirrorUrls(string url)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(url)) return list;
            if (url.IndexOf("edge.forgecdn.net", StringComparison.OrdinalIgnoreCase) >= 0)
                list.Add(url.Replace("edge.forgecdn.net", "mod.mcimirror.top"));
            if (url.IndexOf("media.forgecdn.net", StringComparison.OrdinalIgnoreCase) >= 0)
                list.Add(url.Replace("media.forgecdn.net", "mod.mcimirror.top"));
            if (url.IndexOf("mediafilez.forgecdn.net", StringComparison.OrdinalIgnoreCase) >= 0)
                list.Add(url.Replace("mediafilez.forgecdn.net", "mod.mcimirror.top"));
            return list;
        }

        /// <summary>由文件 ID + 文件名构造 forgecdn 地址（downloadUrl 为空时兜底）。</summary>
        public static string BuildForgeCdnUrl(long fileId, string fileName, bool mirror)
        {
            var first = (fileId / 1000).ToString("0000", CultureInfo.InvariantCulture);
            var second = (fileId % 1000).ToString("000", CultureInfo.InvariantCulture);
            var host = mirror ? "https://mod.mcimirror.top" : "https://edge.forgecdn.net";
            return host + "/files/" + first + "/" + second + "/" +
                   Uri.EscapeDataString(string.IsNullOrWhiteSpace(fileName) ? "file.bin" : fileName);
        }

        /// <summary>一个文件的所有候选下载地址（优先镜像，再官方）。</summary>
        public static List<string> DownloadCandidates(CfFile file)
        {
            var list = new List<string>();
            if (file == null) return list;
            if (!string.IsNullOrWhiteSpace(file.DownloadUrl))
            {
                foreach (var url in MirrorUrls(file.DownloadUrl)) AddUnique(list, url);
                AddUnique(list, file.DownloadUrl);
            }
            AddUnique(list, BuildForgeCdnUrl(file.Id, file.FileName, true));
            AddUnique(list, BuildForgeCdnUrl(file.Id, file.FileName, false));
            return list;
        }

        private static void AddUnique(List<string> list, string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            if (!list.Exists(x => string.Equals(x, url, StringComparison.OrdinalIgnoreCase))) list.Add(url);
        }

        /// <summary>依次尝试候选地址下载。</summary>
        public static async Task DownloadFileAsync(InstallContext ctx, List<string> candidates, string path,
            string sha1, long size, Action<long> onProgress)
        {
            if (candidates == null || candidates.Count == 0)
                throw new Exception("没有可用的下载地址：" + path);
            Exception last = null;
            foreach (var url in candidates)
            {
                try
                {
                    await ctx.Dl.DownloadFileAsync(url, path, sha1, size, onProgress, ctx.Ct, 2, false).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    last = ex;
                    ctx.Warn("下载失败，尝试其它地址：" + url + " :: " + ex.Message);
                }
            }
            throw new Exception("文件下载失败：" + path + " :: " + (last == null ? "未知错误" : last.Message), last);
        }
    }
}
