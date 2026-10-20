using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PCLDownloadCenter
{
    /// <summary>简单日志：写文件 + 触发事件（供 SSE 推送）。</summary>
    public sealed class Logger
    {
        private readonly object _lock = new object();
        private readonly List<string> _tail = new List<string>();
        private readonly string _file;
        public event Action<string> OnLine;

        public Logger(string logDir)
        {
            try
            {
                Directory.CreateDirectory(logDir);
                _file = Path.Combine(logDir, "app-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            }
            catch { _file = null; }
        }

        public void Info(string message) { Write("I", message); }
        public void Warn(string message) { Write("W", message); }
        public void Error(string message, Exception ex = null)
        {
            var text = message;
            if (ex != null) text += " :: " + ex.GetType().Name + ": " + ex.Message + (ex.StackTrace == null ? "" : "\n" + ex.StackTrace);
            Write("E", text);
        }
        public void Debug(string message) { Write("D", message); }

        private void Write(string level, string message)
        {
            if (message == null) message = "";
            var line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + level + " " + message;
            lock (_lock)
            {
                var clean = line.Replace("\r", "").Split('\n');
                foreach (var l in clean)
                {
                    _tail.Add(l);
                    while (_tail.Count > 800) _tail.RemoveAt(0);
                }
                if (_file != null)
                {
                    try { File.AppendAllText(_file, line + Environment.NewLine, Encoding.UTF8); } catch { }
                }
            }
            var handler = OnLine;
            if (handler != null)
            {
                try { handler(line); } catch { }
            }
        }

        public string Tail(int lines)
        {
            lock (_lock)
            {
                if (lines <= 0 || lines > _tail.Count) lines = _tail.Count;
                return string.Join(Environment.NewLine, _tail.GetRange(_tail.Count - lines, lines));
            }
        }

        public string LogFile { get { return _file; } }
    }
}
