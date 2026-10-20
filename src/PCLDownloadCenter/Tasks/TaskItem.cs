using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace PCLDownloadCenter
{
    public enum DcTaskStatus { Pending = 0, Running = 1, Success = 2, Failed = 3, Canceled = 4 }

    /// <summary>一条下载 / 安装任务。</summary>
    public sealed class DcTask
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public string Title { get; set; }
        public string Phase { get; set; }
        public double Progress { get; set; }
        public DcTaskStatus Status { get; set; }
        public string Error { get; set; }
        public string VersionId { get; set; }
        public string Message { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        /// <summary>用于恢复历史任务时显示，重新运行时重置。</summary>
        public List<string> Logs { get; set; }
        public int LogCount { get; set; }

        [JsonIgnore] public CancellationTokenSource Cts { get; set; }
        [JsonIgnore] public CancellationToken Token { get; set; }
        [JsonIgnore] public Func<InstallContext, Task<InstallResult>> Body { get; set; }

        public DcTask()
        {
            Logs = new List<string>();
            CreatedAt = DateTime.Now;
            UpdatedAt = DateTime.Now;
            Status = DcTaskStatus.Pending;
        }

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case DcTaskStatus.Pending: return "等待中";
                    case DcTaskStatus.Running: return "进行中";
                    case DcTaskStatus.Success: return "已完成";
                    case DcTaskStatus.Failed: return "失败";
                    case DcTaskStatus.Canceled: return "已取消";
                    default: return "未知";
                }
            }
        }

        public string ProgressText
        {
            get
            {
                var ratio = Math.Max(0, Math.Min(1, Progress));
                int filled = (int)Math.Round(ratio * 10);
                var sb = new StringBuilder();
                for (int i = 0; i < 10; i++) sb.Append(i < filled ? '█' : '░');
                sb.Append(' ').Append((int)(ratio * 100)).Append('%');
                return sb.ToString();
            }
        }

        public void Log(string line)
        {
            if (line == null) return;
            lock (Logs)
            {
                Logs.Add(DateTime.Now.ToString("HH:mm:ss") + " " + line);
                LogCount++;
                while (Logs.Count > 400) Logs.RemoveAt(0);
            }
            UpdatedAt = DateTime.Now;
        }
    }
}
