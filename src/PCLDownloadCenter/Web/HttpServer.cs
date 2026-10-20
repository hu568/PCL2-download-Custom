using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PCLDownloadCenter
{
    /// <summary>
    /// 极简 HTTP/1.1 服务（TcpListener 实现，避免 HttpListener 的 URL ACL 问题）。
    /// 只监听 127.0.0.1，仅用于本机插件 Web UI。
    /// </summary>
    public sealed class HttpServer
    {
        private readonly int _port;
        private readonly Logger _log;
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        public bool IsRunning { get; private set; }
        public int Port { get { return _port; } }
        public Dictionary<string, Func<HttpRequest, HttpResponse>> Routes =
            new Dictionary<string, Func<HttpRequest, HttpResponse>>(StringComparer.OrdinalIgnoreCase);

        public HttpServer(int port, Logger log)
        {
            _port = port; _log = log;
        }

        public void Start()
        {
            if (IsRunning) return;
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start(256);
            IsRunning = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            Task.Run(() => AcceptLoop(token));
            _log.Info("HTTP 服务已启动：http://127.0.0.1:" + _port + "/");
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            try { if (_cts != null) _cts.Cancel(); } catch { }
            try { if (_listener != null) _listener.Stop(); } catch { }
            _log.Info("HTTP 服务已停止");
        }

        private async Task AcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client = null;
                try { client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (Exception) { if (token.IsCancellationRequested) break; try { await Task.Delay(100).ConfigureAwait(false); } catch { } continue; }
                var c = client;
                var ignored = Task.Run(() => HandleClient(c));
            }
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    client.ReceiveTimeout = 20000;
                    client.SendTimeout = 60000;
                    var req = ReadRequest(stream);
                    if (req == null) return;
                    HttpResponse resp = null;
                    Func<HttpRequest, HttpResponse> handler = null;
                    if (req.UrlPath == "/favicon.ico") resp = HttpResponse.Empty(204);
                    else if (!Routes.TryGetValue(req.UrlPath, out handler))
                    {
                        // 兼容尾部斜杠
                        var alt = req.UrlPath.EndsWith("/") ? req.UrlPath.TrimEnd('/') : req.UrlPath + "/";
                        if (!Routes.TryGetValue(alt, out handler)) handler = null;
                        if (handler == null) resp = HttpResponse.NotFound("接口不存在：" + req.UrlPath);
                    }
                    if (resp == null && handler != null)
                    {
                        try { resp = handler(req); }
                        catch (Exception ex)
                        {
                            _log.Error("处理请求失败：" + req.UrlPath, ex);
                            resp = HttpResponse.Error(ex.Message, 500);
                        }
                    }
                    if (resp == null) resp = HttpResponse.NotFound();
                    WriteResponse(stream, resp);
                }
            }
            catch (Exception ex)
            {
                try { _log.Warn("HTTP 连接异常：" + ex.Message); } catch { }
            }
        }

        private HttpRequest ReadRequest(NetworkStream stream)
        {
            stream.ReadTimeout = 20000;
            var buffer = new List<byte>();
            var tmp = new byte[8192];
            int headerEnd = -1;
            int headerLength = 0;
            while (buffer.Count < 256 * 1024)
            {
                int read = stream.Read(tmp, 0, tmp.Length);
                if (read <= 0) return null;
                for (int i = 0; i < read; i++) buffer.Add(tmp[i]);
                headerEnd = IndexOfHeaderEnd(buffer);
                if (headerEnd >= 0) { headerLength = headerEnd + 4; break; }
            }
            if (headerEnd < 0) return null;
            var head = Encoding.UTF8.GetString(buffer.GetRange(0, headerEnd).ToArray());
            var rest = buffer.GetRange(headerLength, buffer.Count - headerLength);
            var lines = head.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0) return null;
            var parts = lines[0].Split(' ');
            if (parts.Length < 2) return null;
            var req = new HttpRequest { Method = parts[0].ToUpperInvariant() };
            var rawPath = parts[1];
            var qIndex = rawPath.IndexOf('?');
            if (qIndex >= 0)
            {
                req.UrlPath = Uri.UnescapeDataString(rawPath.Substring(0, qIndex));
                req.UrlQuery = rawPath.Substring(qIndex + 1);
                foreach (var kv in req.UrlQuery.Split('&'))
                {
                    if (kv.Length == 0) continue;
                    var eq = kv.IndexOf('=');
                    if (eq < 0) req.Query[Uri.UnescapeDataString(kv)] = "";
                    else req.Query[Uri.UnescapeDataString(kv.Substring(0, eq))] = Uri.UnescapeDataString(kv.Substring(eq + 1));
                }
            }
            else req.UrlPath = Uri.UnescapeDataString(rawPath);
            if (!req.UrlPath.StartsWith("/")) req.UrlPath = "/" + req.UrlPath;

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var key = line.Substring(0, colon).Trim();
                var value = line.Substring(colon + 1).Trim();
                req.Headers[key] = value;
            }
            // 只监听回环地址，天然只能本机访问
            req.ClientIp = "127.0.0.1";

            int contentLength = 0;
            string cl;
            if (req.Headers.TryGetValue("Content-Length", out cl)) int.TryParse(cl, out contentLength);
            if (contentLength > 0)
            {
                var body = new byte[contentLength];
                int already = Math.Min(rest.Count, contentLength);
                for (int i = 0; i < already; i++) body[i] = rest[i];
                int total = already;
                while (total < contentLength)
                {
                    int read = stream.Read(body, total, contentLength - total);
                    if (read <= 0) break;
                    total += read;
                }
                req.BodyBytes = body;
            }
            return req;
        }

        private static int IndexOfHeaderEnd(List<byte> data)
        {
            for (int i = 0; i + 3 < data.Count; i++)
            {
                if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10) return i;
            }
            return -1;
        }

        private void WriteResponse(NetworkStream stream, HttpResponse resp)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(resp.Status).Append(' ').Append(StatusText(resp.Status)).Append("\r\n");
            sb.Append("Server: PCLDownloadCenter\r\n");
            sb.Append("Access-Control-Allow-Origin: *\r\n");
            sb.Append("Access-Control-Allow-Headers: *\r\n");
            sb.Append("Cache-Control: no-store\r\n");
            sb.Append("Connection: close\r\n");
            if (!string.IsNullOrEmpty(resp.ContentType)) sb.Append("Content-Type: ").Append(resp.ContentType).Append("\r\n");
            foreach (var kv in resp.Headers) sb.Append(kv.Key).Append(": ").Append(kv.Value).Append("\r\n");
            if (resp.StreamWriter == null)
                sb.Append("Content-Length: ").Append(resp.Body == null ? 0 : resp.Body.Length).Append("\r\n");
            sb.Append("\r\n");
            var header = Encoding.UTF8.GetBytes(sb.ToString());
            stream.Write(header, 0, header.Length);
            if (resp.StreamWriter != null)
            {
                resp.StreamWriter(stream);
            }
            else if (resp.Body != null && resp.Body.Length > 0)
            {
                stream.Write(resp.Body, 0, resp.Body.Length);
            }
            stream.Flush();
        }

        private static string StatusText(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 204: return "No Content";
                case 302: return "Found";
                case 400: return "Bad Request";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 500: return "Internal Server Error";
                default: return "OK";
            }
        }

        /// <summary>端口是否可连接（判断服务是否在运行）。</summary>
        public static bool IsPortOpen(int port, int timeoutMs = 400)
        {
            try
            {
                using (var c = new TcpClient())
                {
                    var task = c.ConnectAsync(IPAddress.Loopback, port);
                    if (!task.Wait(timeoutMs)) return false;
                    return c.Connected;
                }
            }
            catch { return false; }
        }
    }
}
