using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PCLDownloadCenter
{
    /// <summary>
    /// 任务管理器：串行（可配置并发）执行安装任务，向 API / 主页 / SSE 提供快照。
    /// </summary>
    public sealed class TaskManager
    {
        private readonly object _lock = new object();
        private readonly List<DcTask> _tasks = new List<DcTask>();
        private readonly Queue<DcTask> _queue = new Queue<DcTask>();
        private readonly AppConfig _cfg;
        private readonly Logger _log;
        private readonly Downloader _dl;
        private int _running;
        private DateTime _lastChanged = DateTime.MinValue;
        private DateTime _lastSaved = DateTime.MinValue;

        public event Action Changed;

        public TaskManager(AppConfig cfg, Logger log, Downloader dl)
        {
            _cfg = cfg; _log = log; _dl = dl;
        }

        public int RunningCount { get { lock (_lock) return _running; } }
        public int QueuedCount { get { lock (_lock) return _queue.Count; } }

        public List<DcTask> Snapshot()
        {
            lock (_lock) return _tasks.ToList();
        }

        public DcTask Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            lock (_lock) return _tasks.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public DcTask Create(string kind, string title)
        {
            var task = new DcTask
            {
                Id = DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6),
                Kind = kind ?? "task",
                Title = title ?? kind ?? "任务",
                Phase = "排队中",
                Status = DcTaskStatus.Pending
            };
            task.Log("任务已创建：" + task.Title);
            lock (_lock)
            {
                _tasks.Insert(0, task);
                while (_tasks.Count > 60)
                {
                    var last = _tasks[_tasks.Count - 1];
                    if (last.Status == DcTaskStatus.Running || last.Status == DcTaskStatus.Pending) break;
                    _tasks.RemoveAt(_tasks.Count - 1);
                }
            }
            RaiseChanged(true);
            return task;
        }

        /// <summary>把任务加入执行队列。</summary>
        public void Enqueue(DcTask task, Func<InstallContext, Task<InstallResult>> body)
        {
            if (task == null) throw new ArgumentNullException("task");
            task.Body = body;
            task.Cts = new CancellationTokenSource();
            task.Token = task.Cts.Token;
            lock (_lock) _queue.Enqueue(task);
            RaiseChanged(true);
            Pump();
        }

        private void Pump()
        {
            List<DcTask> started = null;
            lock (_lock)
            {
                while (_running < Math.Max(1, _cfg.MaxConcurrentTasks) && _queue.Count > 0)
                {
                    var task = _queue.Dequeue();
                    _running++;
                    if (started == null) started = new List<DcTask>();
                    started.Add(task);
                }
            }
            if (started == null) return;
            foreach (var task in started)
                Task.Run(() => RunAsync(task));
        }

        private async Task RunAsync(DcTask task)
        {
            var scratch = Path.Combine(Paths.ScratchDir, task.Id);
            try
            {
                FileUtil.EnsureDir(scratch);
                task.Status = DcTaskStatus.Running;
                task.Progress = 0;
                task.Phase = "开始";
                task.Log("开始执行");
                RaiseChanged(true);
                var ctx = CreateContext(task, scratch);
                var result = await task.Body(ctx).ConfigureAwait(false);
                if (result == null) result = InstallResult.Success(task.VersionId, "完成");
                task.Progress = 1;
                if (result.Ok)
                {
                    task.Status = DcTaskStatus.Success;
                    task.Phase = "完成";
                    task.VersionId = result.VersionId ?? task.VersionId;
                    task.Message = result.Message;
                    task.Log("成功：" + (result.Message ?? ""));
                }
                else
                {
                    task.Status = DcTaskStatus.Failed;
                    task.Phase = "失败";
                    task.Error = result.Message;
                    task.Log("失败：" + result.Message);
                }
            }
            catch (OperationCanceledException)
            {
                task.Status = DcTaskStatus.Canceled;
                task.Phase = "已取消";
                task.Log("任务已取消");
            }
            catch (Exception ex)
            {
                task.Status = DcTaskStatus.Failed;
                task.Phase = "失败";
                task.Error = ex.Message;
                task.Log("异常：" + ex.GetType().Name + " " + ex.Message);
                _log.Error("任务执行失败：" + task.Title, ex);
            }
            finally
            {
                task.UpdatedAt = DateTime.Now;
                if (!_cfg.KeepScratch) FileUtil.SafeDeleteDir(scratch);
                lock (_lock) _running--;
                RaiseChanged(true);
                Save(true);
                Pump();
            }
        }

        public InstallContext CreateContext(DcTask task, string scratch = null)
        {
            var ctx = new InstallContext
            {
                Cfg = _cfg,
                Log = _log,
                Dl = _dl,
                McDir = Paths.McDir,
                ScratchDir = scratch ?? Path.Combine(Paths.ScratchDir, task == null ? Guid.NewGuid().ToString("N") : task.Id),
                Ct = task == null ? CancellationToken.None : task.Token
            };
            if (task != null)
            {
                ctx.ProgressCallback = (phase, ratio) =>
                {
                    task.Phase = phase;
                    if (ratio >= 0) task.Progress = Math.Max(0, Math.Min(1, ratio));
                    task.UpdatedAt = DateTime.Now;
                    RaiseChanged(false);
                };
                ctx.InfoCallback = (line) => { task.Log(line); RaiseChanged(false); };
            }
            return ctx;
        }

        public bool Cancel(string id)
        {
            var task = Find(id);
            if (task == null) return false;
            try
            {
                if (task.Cts != null) task.Cts.Cancel();
                if (task.Status == DcTaskStatus.Pending)
                {
                    lock (_lock) { if (_queue.Contains(task)) return true; }
                    task.Status = DcTaskStatus.Canceled;
                    task.Phase = "已取消";
                    RaiseChanged(true);
                }
                return true;
            }
            catch { return false; }
        }

        public void ClearFinished()
        {
            lock (_lock) _tasks.RemoveAll(t => t.Status == DcTaskStatus.Success || t.Status == DcTaskStatus.Failed || t.Status == DcTaskStatus.Canceled);
            RaiseChanged(true);
        }

        /// <summary>节流触发变更事件（供主页重写与 SSE）。</summary>
        public void RaiseChanged(bool force)
        {
            var now = DateTime.Now;
            if (!force && (now - _lastChanged).TotalMilliseconds < 400) return;
            _lastChanged = now;
            var handler = Changed;
            if (handler != null) { try { handler(); } catch { } }
            if (force) Save();
        }

        public void Save() { Save(false); }

        public void Save(bool force)
        {
            var now = DateTime.Now;
            if (!force && (now - _lastSaved).TotalSeconds < 2) return;
            _lastSaved = now;
            try
            {
                List<DcTask> snapshot;
                lock (_lock) snapshot = _tasks.Take(30).ToList();
                var copy = snapshot.Select(t => new DcTask
                {
                    Id = t.Id, Kind = t.Kind, Title = t.Title, Phase = t.Phase, Progress = t.Progress,
                    Status = t.Status, Error = t.Error, VersionId = t.VersionId, Message = t.Message,
                    CreatedAt = t.CreatedAt, UpdatedAt = t.UpdatedAt, Logs = t.Logs.Skip(Math.Max(0, t.Logs.Count - 50)).ToList(), LogCount = t.LogCount
                }).ToList();
                JsonUtil.WriteFile(Paths.TasksFile, copy, true);
            }
            catch { }
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(Paths.TasksFile)) return;
                var list = JsonUtil.ReadFile<List<DcTask>>(Paths.TasksFile, null);
                if (list == null) return;
                lock (_lock)
                {
                    foreach (var t in list)
                    {
                        if (t.Status == DcTaskStatus.Running || t.Status == DcTaskStatus.Pending)
                        {
                            t.Status = DcTaskStatus.Failed;
                            t.Phase = "已中断";
                            t.Error = "服务重启，任务中断";
                        }
                        _tasks.Add(t);
                    }
                }
            }
            catch { }
        }
    }
}