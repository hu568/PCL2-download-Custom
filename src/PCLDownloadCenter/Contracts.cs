using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>一个待下载文件。</summary>
    public sealed class DownloadItem
    {
        public string Url { get; set; }
        public string Path { get; set; }
        public string Sha1 { get; set; }
        public long Size { get; set; } = -1;
        public string Name { get; set; }

        public DownloadItem() { }
        public DownloadItem(string url, string path, string sha1 = null, long size = -1, string name = null)
        {
            Url = url; Path = path; Sha1 = sha1; Size = size; Name = name ?? (path == null ? null : System.IO.Path.GetFileName(path));
        }
        public override string ToString() => Name ?? Url;
    }

    /// <summary>安装结果。</summary>
    public sealed class InstallResult
    {
        public bool Ok { get; set; }
        public string VersionId { get; set; }
        public string Message { get; set; }

        public static InstallResult Success(string versionId, string message = null)
        {
            return new InstallResult { Ok = true, VersionId = versionId, Message = message ?? ("安装完成：" + versionId) };
        }
        public static InstallResult Fail(string message)
        {
            return new InstallResult { Ok = false, Message = message };
        }
    }

    /// <summary>
    /// 安装上下文：所有安装模块都通过它拿到配置、下载器、日志、进度与取消能力。
    /// </summary>
    public sealed class InstallContext
    {
        public AppConfig Cfg { get; set; }
        public Logger Log { get; set; }
        public Downloader Dl { get; set; }
        public CancellationToken Ct { get; set; }
        public string McDir { get; set; }
        /// <summary>临时目录（安装结束由任务管理器清理）。</summary>
        public string ScratchDir { get; set; }
        public Action<string, double> ProgressCallback { get; set; }
        public Action<string> InfoCallback { get; set; }

        public string VersionsDir => System.IO.Path.Combine(McDir, "versions");
        public string LibrariesDir => System.IO.Path.Combine(McDir, "libraries");
        public string AssetsDir => System.IO.Path.Combine(McDir, "assets");

        /// <summary>上报阶段进度；ratio 取 0~1。</summary>
        public void Progress(string phase, double ratio)
        {
            if (ProgressCallback != null) ProgressCallback(phase, ratio);
        }
        /// <summary>向任务日志写一行。</summary>
        public void Info(string message)
        {
            if (InfoCallback != null) InfoCallback(message);
            if (Log != null) Log.Info(message);
        }
        public void Warn(string message)
        {
            if (InfoCallback != null) InfoCallback("[警告] " + message);
            if (Log != null) Log.Warn(message);
        }
        public void ThrowIfCancelled() { Ct.ThrowIfCancellationRequested(); }
        public string Scratch(string name)
        {
            Directory.CreateDirectory(ScratchDir);
            return System.IO.Path.Combine(ScratchDir, name);
        }
    }

    /// <summary>简化版 HTTP 请求。</summary>
    public sealed class HttpRequest
    {
        public string Method = "GET";
        public string UrlPath = "/";
        public string UrlQuery = "";
        public Dictionary<string, string> Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public byte[] BodyBytes = new byte[0];
        public string ClientIp = "127.0.0.1";

        public string BodyText
        {
            get { return BodyBytes == null ? "" : Encoding.UTF8.GetString(BodyBytes); }
        }
        public string Get(string name)
        {
            string v;
            return Query.TryGetValue(name, out v) ? v : null;
        }
        public int GetInt(string name, int fallback)
        {
            int v;
            return int.TryParse(Get(name), out v) ? v : fallback;
        }
        public JObject JsonBody()
        {
            string text = BodyText;
            if (string.IsNullOrWhiteSpace(text)) return new JObject();
            try { return JObject.Parse(text); }
            catch { return new JObject(); }
        }
        public bool IsLocal
        {
            get { return ClientIp == "127.0.0.1" || ClientIp == "::1" || ClientIp == "localhost"; }
        }
    }

    /// <summary>简化版 HTTP 响应。</summary>
    public sealed class HttpResponse
    {
        public int Status = 200;
        public string ContentType = "application/json; charset=utf-8";
        public byte[] Body = new byte[0];
        /// <summary>若不为 null，则由它向连接写入响应体（SSE / 大文件）。</summary>
        public Action<Stream> StreamWriter = null;
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static HttpResponse Json(object obj)
        {
            return new HttpResponse { ContentType = "application/json; charset=utf-8", Body = Encoding.UTF8.GetBytes(JsonUtil.Serialize(obj)) };
        }
        public static HttpResponse JToken(JToken token)
        {
            return new HttpResponse { ContentType = "application/json; charset=utf-8", Body = Encoding.UTF8.GetBytes(token.ToString(Formatting.None)) };
        }
        public static HttpResponse Text(string text, string contentType = null)
        {
            return new HttpResponse { ContentType = contentType ?? "text/plain; charset=utf-8", Body = Encoding.UTF8.GetBytes(text ?? "") };
        }
        public static HttpResponse Html(string html)
        {
            return new HttpResponse { ContentType = "text/html; charset=utf-8", Body = Encoding.UTF8.GetBytes(html ?? "") };
        }
        public static HttpResponse Bytes(byte[] data, string contentType)
        {
            return new HttpResponse { ContentType = contentType, Body = data ?? new byte[0] };
        }
        public static HttpResponse Empty(int status = 204)
        {
            return new HttpResponse { Status = status, ContentType = "text/plain; charset=utf-8", Body = new byte[0] };
        }
        public static HttpResponse Error(string message, int status = 400)
        {
            return new HttpResponse { Status = status, ContentType = "application/json; charset=utf-8", Body = Encoding.UTF8.GetBytes(JsonUtil.Serialize(new { ok = false, error = message ?? "error" })) };
        }
        public static HttpResponse NotFound(string message = "Not Found")
        {
            return Error(message, 404);
        }
        public static HttpResponse Redirect(string location)
        {
            var r = Empty(302);
            r.Headers["Location"] = location;
            return r;
        }
        /// <summary>Server-Sent Events 流。</summary>
        public static HttpResponse Sse(Action<Stream> writer)
        {
            var r = new HttpResponse { ContentType = "text/event-stream; charset=utf-8", StreamWriter = writer };
            r.Headers["Cache-Control"] = "no-cache";
            r.Headers["Connection"] = "keep-alive";
            return r;
        }
    }

    /// <summary>主页渲染所需的状态快照（由 HomepageRenderer 组装，Program 不依赖细节）。</summary>
    public sealed class HomepageState
    {
        public string View = "home";
        public int VersionPage = 1;
        public bool ServerRunning;
        public string ServerUrl;
        public string StatusText = "";
        public List<DcTask> Tasks = new List<DcTask>();
        public List<string> InstalledVersions = new List<string>();
        public object Versions = null;   // ManifestData（避免强类型耦合，由 HomepageRenderer 断言）
        public string LastError = "";
        public DateTime GeneratedAt = DateTime.Now;
    }
}
