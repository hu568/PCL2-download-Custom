using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>
    /// 下载器：支持并发、断点重试、SHA1 校验、镜像回退。
    /// </summary>
    public sealed class Downloader
    {
        private readonly Logger _log;
        private readonly AppConfig _cfg;
        private readonly HttpClient _http;
        private readonly ConcurrentDictionary<string, bool> _failedHosts = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public const string UserAgent = "PCLDownloadCenter/1.0 (+https://github.com/; PCL homepage plugin)";

        static Downloader()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch { }
            try { ServicePointManager.DefaultConnectionLimit = 128; } catch { }
            try { ServicePointManager.Expect100Continue = false; } catch { }
        }

        public Downloader(Logger log, AppConfig cfg)
        {
            _log = log;
            _cfg = cfg;
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                MaxConnectionsPerServer = 128,
                UseProxy = true
            };
            _http = new HttpClient(handler);
            _http.Timeout = TimeSpan.FromMinutes(10);
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        }

        public AppConfig Config { get { return _cfg; } }

        private static CancellationTokenSource Link(CancellationToken ct, int timeoutMs)
        {
            var cts = new CancellationTokenSource();
            if (timeoutMs > 0) cts.CancelAfter(timeoutMs);
            if (ct.CanBeCanceled)
            {
                ct.Register(() => { try { cts.Cancel(); } catch { } });
            }
            return cts;
        }

        /// <summary>带镜像回退的候选地址列表。</summary>
        public List<string> Candidates(string url, bool allowMirror)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(url)) return list;
            var primary = SourceRewriter.Apply(url, _cfg.DownloadSource);
            list.Add(primary);
            if (!string.Equals(primary, url, StringComparison.OrdinalIgnoreCase)) list.Add(url);
            if (allowMirror && _cfg.UseMirrorFallback)
            {
                foreach (var m in SourceRewriter.GetMirrorCandidates(url))
                    if (!list.Exists(x => string.Equals(x, m, StringComparison.OrdinalIgnoreCase))) list.Add(m);
                foreach (var m in SourceRewriter.GetMirrorCandidates(primary))
                    if (!list.Exists(x => string.Equals(x, m, StringComparison.OrdinalIgnoreCase))) list.Add(m);
            }
            return list;
        }

        public async Task<string> GetStringAsync(string url, CancellationToken ct = default(CancellationToken), int timeoutMs = 30000, int retry = 3, bool allowMirror = true)
        {
            var bytes = await GetBytesAsync(url, ct, timeoutMs, retry, allowMirror).ConfigureAwait(false);
            return Encoding.UTF8.GetString(bytes);
        }

        public async Task<JObject> GetJsonAsync(string url, CancellationToken ct = default(CancellationToken), int timeoutMs = 30000, int retry = 3, bool allowMirror = true)
        {
            var text = await GetStringAsync(url, ct, timeoutMs, retry, allowMirror).ConfigureAwait(false);
            return JObject.Parse(text);
        }

        public async Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default(CancellationToken), int timeoutMs = 30000, int retry = 3, bool allowMirror = true)
        {
            Exception last = null;
            var candidates = Candidates(url, allowMirror);
            foreach (var candidate in candidates)
            {
                for (int attempt = 1; attempt <= Math.Max(1, retry); attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using (var cts = Link(ct, timeoutMs))
                        using (var resp = await _http.GetAsync(candidate, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                        {
                            resp.EnsureSuccessStatusCode();
                            return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        last = ex;
                        _log.Warn("请求失败（第 " + attempt + " 次）：" + candidate + " :: " + ex.Message);
                        await Task.Delay(200 * attempt, ct).ConfigureAwait(false);
                    }
                }
            }
            throw new Exception("网络请求失败：" + url + " :: " + (last == null ? "未知错误" : last.Message), last);
        }

        public async Task<string> PostJsonAsync(string url, string jsonBody, CancellationToken ct = default(CancellationToken), int timeoutMs = 60000, int retry = 2, bool allowMirror = false)
        {
            var candidates = Candidates(url, allowMirror);
            Exception last = null;
            foreach (var candidate in candidates)
            {
                for (int attempt = 1; attempt <= Math.Max(1, retry); attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using (var cts = Link(ct, timeoutMs))
                        using (var content = new StringContent(jsonBody ?? "{}", Encoding.UTF8, "application/json"))
                        using (var req = new HttpRequestMessage(HttpMethod.Post, candidate) { Content = content })
                        using (var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false))
                        {
                            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            if (!resp.IsSuccessStatusCode)
                                throw new Exception("HTTP " + (int)resp.StatusCode + " " + text);
                            return text;
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        last = ex;
                        await Task.Delay(200 * attempt, ct).ConfigureAwait(false);
                    }
                }
            }
            throw new Exception("POST 请求失败：" + url + " :: " + (last == null ? "未知错误" : last.Message), last);
        }

        /// <summary>
        /// 下载单个文件，返回最终使用的 URL。
        /// </summary>
        public async Task<string> DownloadFileAsync(string url, string path, string sha1 = null, long size = -1,
            Action<long> onProgress = null, CancellationToken ct = default(CancellationToken), int retry = 3, bool allowMirror = true)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException("path");
            // 已存在且校验通过则跳过
            if (File.Exists(path) && !string.IsNullOrWhiteSpace(sha1))
            {
                if (string.Equals(FileUtil.Sha1OfFile(path), sha1, StringComparison.OrdinalIgnoreCase))
                {
                    if (onProgress != null) onProgress(size > 0 ? size : FileUtil.SizeOf(path));
                    return url;
                }
            }
            else if (File.Exists(path) && string.IsNullOrWhiteSpace(sha1) && size > 0 && FileUtil.SizeOf(path) == size)
            {
                if (onProgress != null) onProgress(size);
                return url;
            }

            FileUtil.EnsureDir(Path.GetDirectoryName(path));
            var candidates = Candidates(url, allowMirror);
            Exception last = null;
            foreach (var candidate in candidates)
            {
                for (int attempt = 1; attempt <= Math.Max(1, retry); attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    var part = path + ".part";
                    FileUtil.SafeDelete(part);
                    try
                    {
                        // 头部超时 60s；数据停滞超时 45s（收到数据就重置），避免连接假死时长时间卡住
                        using (var headCts = Link(ct, 60000))
                        using (var resp = await _http.GetAsync(candidate, HttpCompletionOption.ResponseHeadersRead, headCts.Token).ConfigureAwait(false))
                        {
                            resp.EnsureSuccessStatusCode();
                            var total = resp.Content.Headers.ContentLength.HasValue ? resp.Content.Headers.ContentLength.Value : size;
                            using (var stallCts = new CancellationTokenSource())
                            using (var reg = ct.Register(() => { try { stallCts.Cancel(); } catch { } }))
                            using (var input = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true))
                            {
                                stallCts.CancelAfter(45000);
                                var buffer = new byte[1024 * 128];
                                long done = 0;
                                int read;
                                while (true)
                                {
                                    read = await input.ReadAsync(buffer, 0, buffer.Length, stallCts.Token).ConfigureAwait(false);
                                    if (read <= 0) break;
                                    await output.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                                    done += read;
                                    try { stallCts.CancelAfter(45000); } catch { }
                                    if (onProgress != null) onProgress(done);
                                }
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(sha1))
                        {
                            var actual = FileUtil.Sha1OfFile(part);
                            if (!string.Equals(actual, sha1, StringComparison.OrdinalIgnoreCase))
                                throw new Exception("SHA1 不匹配：期望 " + sha1 + "，实际 " + actual);
                        }
                        if (size > 0 && FileUtil.SizeOf(part) != size)
                            throw new Exception("文件大小不匹配：期望 " + size + "，实际 " + FileUtil.SizeOf(part));
                        FileUtil.SafeDelete(path);
                        File.Move(part, path);
                        return candidate;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        FileUtil.SafeDelete(part);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        FileUtil.SafeDelete(part);
                        _log.Warn("下载失败（" + attempt + "/" + retry + "）：" + candidate + " → " + path + " :: " + ex.Message);
                        if (attempt < retry) await Task.Delay(300 * attempt, ct).ConfigureAwait(false);
                    }
                }
                if (candidates.Count > 1)
                    _log.Warn("切换下载源重试：" + path);
            }
            throw new Exception("下载失败：" + url + " :: " + (last == null ? "未知错误" : last.Message), last);
        }

        /// <summary>
        /// 批量下载。onProgress(已完成数, 总数)。
        /// </summary>
        public async Task DownloadManyAsync(IList<DownloadItem> items, Action<int, int> onProgress = null,
            CancellationToken ct = default(CancellationToken), Action<string> onItemFailed = null, int threads = 0)
        {
            if (items == null || items.Count == 0) return;
            if (threads <= 0) threads = _cfg.Threads;
            var total = items.Count;
            var completed = 0;
            var failures = new ConcurrentBag<string>();
            var sem = new SemaphoreSlim(threads);
            var tasks = new List<Task>();
            foreach (var item in items)
            {
                await sem.WaitAsync(ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await DownloadFileAsync(item.Url, item.Path, item.Sha1, item.Size, null, ct, 3, true).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        failures.Add(item.Url + " → " + (item.Path ?? "") + " :: " + ex.Message);
                        if (onItemFailed != null) { try { onItemFailed(item.Url); } catch { } }
                    }
                    finally
                    {
                        sem.Release();
                        var done = Interlocked.Increment(ref completed);
                        if (onProgress != null) { try { onProgress(done, total); } catch { } }
                    }
                }, ct));
            }
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            if (!failures.IsEmpty)
            {
                var sb = new StringBuilder();
                sb.AppendLine("有 " + failures.Count + " 个文件下载失败：");
                int i = 0;
                foreach (var f in failures) { if (i++ >= 8) { sb.AppendLine("……"); break; } sb.AppendLine("- " + f); }
                throw new Exception(sb.ToString().Trim());
            }
        }

        /// <summary>探测地址是否可达。</summary>
        public async Task<bool> CheckAsync(string url, int timeoutMs = 8000)
        {
            try
            {
                using (var cts = Link(CancellationToken.None, timeoutMs))
                using (var req = new HttpRequestMessage(HttpMethod.Head, url))
                using (var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false))
                    return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }
    }
}
