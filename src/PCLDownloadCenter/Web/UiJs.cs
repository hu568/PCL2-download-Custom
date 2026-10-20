// Web UI 交互脚本（内嵌到程序集，原生 JS，无任何外部依赖）
namespace PCLDownloadCenter
{
    internal static class UiJs
    {
        public const string Text = """
(function () {
  'use strict';

  // ---------- 错误采集（便于自动化测试断言） ----------
  window.__uiErrors = [];
  window.addEventListener('error', function (e) {
    window.__uiErrors.push(String((e && e.message) || 'error'));
  });
  window.addEventListener('unhandledrejection', function (e) {
    var r = e && e.reason;
    window.__uiErrors.push('promise: ' + String((r && r.message) || r));
  });

  var VIEWS = ['home', 'vanilla', 'loader', 'pack', 'tasks', 'settings', 'about'];

  var state = {
    view: 'home',
    status: null,
    config: null,
    about: null,
    tasks: [],
    running: 0,
    queued: 0,
    tasksSig: '',
    taskLogs: {},
    openLogs: {},
    installed: [],
    latestRelease: '',
    latestSnapshot: '',
    releaseVersions: null,
    manifest: { filter: 'release', query: '', page: 1, pageSize: 50, dataKey: '', data: null, summary: null },
    loader: { loading: false, versions: null, error: '' },
    pack: { query: '', offset: 0, limit: 20, total: 0, hits: [], loading: false, error: '', queried: false, gameVersion: '', loader: '' },
    sse: null,
    sseOk: false,
    lastSseAt: 0,
    pollTimer: null
  };
  window.__pcl = { state: state };

  // ---------- 基础工具 ----------
  function $(sel, root) { return (root || document).querySelector(sel); }
  function $$(sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); }

  function esc(value) {
    return String(value === null || value === undefined ? '' : value).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  function fmtNum(n) {
    n = Number(n) || 0;
    if (n >= 100000000) return (n / 100000000).toFixed(1) + ' 亿';
    if (n >= 10000) return (n / 10000).toFixed(1) + ' 万';
    if (n >= 1000) return String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
    return String(n);
  }

  function fmtDuration(sec) {
    sec = Math.max(0, Number(sec) || 0);
    var h = Math.floor(sec / 3600), m = Math.floor((sec % 3600) / 60), s = Math.floor(sec % 60);
    if (h > 0) return h + ' 小时 ' + m + ' 分钟';
    if (m > 0) return m + ' 分钟 ' + s + ' 秒';
    return s + ' 秒';
  }

  function debounce(fn, wait) {
    var timer = null;
    return function () {
      var args = arguments, self = this;
      if (timer) clearTimeout(timer);
      timer = setTimeout(function () { timer = null; fn.apply(self, args); }, wait || 300);
    };
  }

  function emptyHtml(icon, text) {
    return '<div class="empty"><span class="big">' + icon + '</span>' + esc(text) + '</div>';
  }

  function errorHtml(text, retryAct) {
    return '<div class="error-box"><div class="msg">' + esc(text) + '</div>' +
      (retryAct ? '<button class="btn sm" data-act="' + retryAct + '">重试</button>' : '') + '</div>';
  }

  function cssEsc(value) {
    return String(value === null || value === undefined ? '' : value).replace(/["\\]/g, '\\$&');
  }

  // ---------- 网络 ----------
  async function api(path, options) {
    var opt = options || {};
    var init = { method: opt.method || 'GET', headers: { 'Accept': 'application/json' } };
    if (opt.body !== undefined) {
      init.headers['Content-Type'] = 'application/json';
      init.body = typeof opt.body === 'string' ? opt.body : JSON.stringify(opt.body);
    }
    var res;
    try {
      res = await fetch(path, init);
    } catch (e) {
      throw new Error('无法连接本地服务，请确认 PCL 下载中心后台正在运行');
    }
    var text = '';
    try { text = await res.text(); } catch (e) { text = ''; }
    var data = null;
    try { data = text ? JSON.parse(text) : {}; } catch (e) {
      throw new Error('服务返回了无法解析的内容（HTTP ' + res.status + '）');
    }
    if (!res.ok || (data && data.ok === false)) {
      var err = new Error((data && data.error) || ('请求失败：HTTP ' + res.status));
      err.data = data;
      throw err;
    }
    return data || {};
  }

  function toast(message, type, title) {
    var box = $('#toasts');
    if (!box) return;
    var el = document.createElement('div');
    el.className = 'toast ' + (type || 'info');
    var head = title || (type === 'error' ? '出错了' : (type === 'success' ? '完成' : '提示'));
    el.innerHTML = '<div class="t-title">' + esc(head) + '</div><div class="t-body">' + esc(message) + '</div>';
    var remove = function () { if (el.parentNode) el.parentNode.removeChild(el); };
    el.addEventListener('click', remove);
    box.appendChild(el);
    setTimeout(remove, type === 'error' ? 8000 : 3600);
    while (box.children.length > 4) box.removeChild(box.firstChild);
  }

  function copyText(text) {
    if (!text) return;
    var done = function () { toast(String(text), 'success', '已复制'); };
    var fallback = function () {
      try {
        var ta = document.createElement('textarea');
        ta.value = text;
        ta.style.position = 'fixed';
        ta.style.opacity = '0';
        document.body.appendChild(ta);
        ta.select();
        document.execCommand('copy');
        document.body.removeChild(ta);
        done();
      } catch (e) { toast('复制失败，请手动选择文本', 'error'); }
    };
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text).then(done, fallback);
    } else fallback();
  }

  function busy(btn, on, text) {
    if (!btn) return;
    if (on) {
      btn.dataset.orig = btn.innerHTML;
      btn.disabled = true;
      btn.classList.add('spinning');
      btn.innerHTML = text || '处理中…';
    } else {
      btn.disabled = false;
      btn.classList.remove('spinning');
      if (btn.dataset.orig) { btn.innerHTML = btn.dataset.orig; delete btn.dataset.orig; }
    }
  }

  // ---------- 视图切换 ----------
  function viewFromHash() {
    var h = String(location.hash || '').replace(/^#/, '').replace(/^\//, '');
    var name = h.split('/')[0].split('?')[0];
    return VIEWS.indexOf(name) >= 0 ? name : 'home';
  }

  function showView(name, skipLoad) {
    if (VIEWS.indexOf(name) < 0) name = 'home';
    state.view = name;
    $$('#navTabs .tab').forEach(function (tab) {
      tab.classList.toggle('active', tab.getAttribute('data-view') === name);
    });
    VIEWS.forEach(function (v) {
      var el = $('#view-' + v);
      if (el) el.classList.toggle('active', v === name);
    });
    try { history.replaceState(null, '', '#' + name); } catch (e) { location.hash = name; }
    try { window.scrollTo(0, 0); } catch (e) { }
    if (skipLoad) return;
    if (name === 'home') loadHome();
    else if (name === 'vanilla') loadVanilla();
    else if (name === 'loader') loadLoaderPage();
    else if (name === 'pack') loadPackPage();
    else if (name === 'tasks') { renderTasks(); refreshTasks(); }
    else if (name === 'settings') loadSettings();
    else if (name === 'about') loadAbout();
  }

  // ---------- 状态 / 连接 ----------
  function setOnline(ok) {
    var dot = $('#statusDot'), text = $('#statusText');
    if (!dot || !text) return;
    if (ok) { dot.className = 'dot ok'; text.textContent = '服务运行中'; }
    else { dot.className = 'dot bad'; text.textContent = '服务未连接'; }
  }

  async function loadStatus() {
    try {
      var data = await api('/api/status');
      state.status = data;
      setOnline(true);
      var sub = $('#brandSub');
      if (sub) sub.textContent = '服务端口 ' + data.port + ' · v' + (data.version || '');
      var installed = (data.installedVersions || []).length;
      var si = $('#statInstalled');
      if (si) si.textContent = installed;
      var src = data.downloadSource === 'bmclapi' ? 'BMCLAPI 镜像' : '官方源';
      var ss = $('#statSource');
      if (ss) ss.textContent = src;
      var sm = $('#statMc');
      if (sm) sm.textContent = data.mcDir || '-';
      updateTaskCounters(data.runningTasks || 0, data.queuedTasks || 0);
      if (state.view === 'home' && !state.installed.length) loadInstalled();
    } catch (e) {
      setOnline(false);
      var sub = $('#brandSub');
      if (sub) sub.textContent = '无法连接本地服务（' + e.message + '）';
    }
  }

  function updateTaskCounters(running, queued) {
    state.running = running || 0;
    state.queued = queued || 0;
    var r = $('#runningCount'), q = $('#queuedCount');
    if (r) r.textContent = state.running;
    if (q) q.textContent = state.queued;
    var badge = $('#tabTaskBadge');
    if (badge) {
      var active = state.running + state.queued;
      if (active > 0) { badge.hidden = false; badge.textContent = active; }
      else badge.hidden = true;
    }
    var s = $('#statRunning');
    if (s) s.textContent = state.running + ' / ' + state.queued;
    var summary = $('#taskSummary');
    if (summary && state.tasks.length) summary.textContent = '共 ' + state.tasks.length + ' 个，进行中 ' + state.running + '，排队 ' + state.queued;
  }

  // ---------- SSE ----------
  function startPolling() {
    if (state.pollTimer) return;
    refreshTasks();
    loadStatus();
    state.pollTimer = setInterval(function () {
      refreshTasks();
      loadStatus();
    }, 2500);
  }

  function stopPolling() {
    if (state.pollTimer) { clearInterval(state.pollTimer); state.pollTimer = null; }
  }

  function connectSse() {
    if (!window.EventSource) { startPolling(); return; }
    var es;
    try { es = new EventSource('/api/events'); } catch (e) { startPolling(); return; }
    state.sse = es;
    es.onopen = function () {
      state.sseOk = true;
      state.lastSseAt = Date.now();
      setOnline(true);
      stopPolling();
    };
    es.onerror = function () {
      state.sseOk = false;
      startPolling();
    };
    es.addEventListener('hello', function (ev) {
      state.sseOk = true;
      state.lastSseAt = Date.now();
      setOnline(true);
      stopPolling();
      var d = null;
      try { d = JSON.parse(ev.data); } catch (e) { return; }
      var sub = $('#brandSub');
      if (sub && d) sub.textContent = '服务端口 ' + d.port + ' · v' + (d.version || '');
    });
    es.addEventListener('ready', function () { state.lastSseAt = Date.now(); });
    es.addEventListener('ping', function () { state.lastSseAt = Date.now(); });
    es.addEventListener('tasks', function (ev) {
      state.sseOk = true;
      state.lastSseAt = Date.now();
      var d = null;
      try { d = JSON.parse(ev.data); } catch (e) { return; }
      setTasks(d.tasks, d.running, d.queued);
    });
    es.addEventListener('log', function (ev) {
      var d = null;
      try { d = JSON.parse(ev.data); } catch (e) { return; }
      appendGlobalLog(d && d.line);
    });
  }

  // ---------- 任务 ----------
  function setTasks(tasks, running, queued) {
    tasks = tasks || [];
    tasks.forEach(function (t) {
      if (t.logs && t.logs.length) state.taskLogs[t.id] = t.logs;
      else if (state.taskLogs[t.id]) t.logs = state.taskLogs[t.id];
    });
    state.tasks = tasks;
    updateTaskCounters(running || 0, queued || 0);
    var sig = JSON.stringify(tasks.map(function (t) {
      return [t.id, t.status, Math.round((Number(t.progress) || 0) * 1000), t.phase, t.logCount, t.error, t.message];
    }));
    if (sig === state.tasksSig) return;
    state.tasksSig = sig;
    if (state.view === 'tasks') renderTasks();
    else if (state.view === 'home') renderHomeTasks();
  }

  async function refreshTasks() {
    try {
      var d = await api('/api/tasks');
      setTasks(d.tasks, d.running, d.queued);
      if (state.view === 'about') loadLogsQuiet();
    } catch (e) {
      setOnline(false);
    }
  }

  function logSummaryText(total, shown) {
    total = total || shown || 0;
    if (shown && total > shown) return '日志（最近 ' + shown + ' 行 / 共 ' + total + ' 行）';
    return '日志（' + total + ' 行）';
  }

  function statusClass(status) {
    if (status === 'Success') return 'green';
    if (status === 'Failed') return 'red';
    if (status === 'Running') return 'blue';
    return 'gray';
  }

  function taskCardHtml(t) {
    var pct = Math.round(Math.max(0, Math.min(1, Number(t.progress) || 0)) * 100);
    var cls = statusClass(t.status);
    var fill = t.status === 'Success' ? ' done' : (t.status === 'Failed' ? ' failed' : (t.status === 'Canceled' ? ' canceled' : ''));
    var logs = state.taskLogs[t.id] || t.logs || [];
    var open = state.openLogs[t.id] ? ' open' : '';
    var canCancel = t.status === 'Running' || t.status === 'Pending';
    var html = '<div class="task">' +
      '<div class="task-head">' +
        '<div style="min-width:0;flex:1">' +
          '<div class="task-title">' + esc(t.title) + '</div>' +
          '<div class="task-meta">' +
            '<span class="badge ' + cls + '">' + esc(t.statusText || t.status) + '</span>' +
            (t.kind ? '<span class="badge gray">' + esc(t.kind) + '</span>' : '') +
            (t.versionId ? '<span class="muted small">版本：' + esc(t.versionId) + '</span>' : '') +
            '<span class="muted small mono">' + esc(t.id) + '</span>' +
          '</div>' +
        '</div>' +
        '<div class="task-actions">' +
          (canCancel ? '<button class="btn sm danger" data-act="task-cancel" data-id="' + esc(t.id) + '">取消</button>' : '') +
          ((t.status === 'Success' && t.versionId) ? '<button class="btn sm" data-act="goto" data-view="vanilla">再装一个</button>' : '') +
        '</div>' +
      '</div>' +
      '<div class="task-body">' +
        '<div class="task-phase"><span>' + esc(t.phase || '') + '</span><span>' + esc(t.progressText || (pct + '%')) + '</span></div>' +
        '<div class="progress"><div class="progress-fill' + fill + '" style="width:' + pct + '%"></div></div>' +
      '</div>';
    if (t.error) html += '<div class="task-error">' + esc(t.error) + '</div>';
    else if (t.status === 'Success') {
      html += '<div class="task-success">' + esc(t.message || '安装完成') +
        (t.versionId ? '<br>已安装到 <span class="code">versions/' + esc(t.versionId) + '</span>' : '') + '</div>';
    }
    html += '<details class="logs" data-id="' + esc(t.id) + '"' + open + '>' +
      '<summary>' + logSummaryText(t.logCount, logs.length) + '</summary>' +
      '<pre class="log-pre">' + (logs.length ? logs.map(esc).join('\n') : '（暂无日志）') + '</pre>' +
      '</details>';
    html += '</div>';
    return html;
  }

  function renderTasks() {
    var box = $('#taskList');
    if (!box) return;
    var list = state.tasks || [];
    var summary = $('#taskSummary');
    if (summary) summary.textContent = list.length ? ('共 ' + list.length + ' 个，进行中 ' + state.running + '，排队 ' + state.queued) : '';
    if (!list.length) { box.innerHTML = emptyHtml('📋', '暂无任务，去「原版游戏」页安装一个版本吧。'); return; }
    var html = '<div class="task-list">';
    list.forEach(function (t) { html += taskCardHtml(t); });
    html += '</div>';
    box.innerHTML = html;
  }

  function renderHomeTasks() {
    var box = $('#homeTasks');
    if (!box) return;
    var list = state.tasks || [];
    if (!list.length) {
      box.innerHTML = emptyHtml('🗒️', '还没有任务，去「原版游戏」页安装一个版本试试。');
      return;
    }
    var html = '<div class="list">';
    list.slice(0, 4).forEach(function (t) {
      var cls = statusClass(t.status);
      html += '<div class="list-row">' +
        '<div style="min-width:0">' +
          '<div style="font-weight:600">' + esc(t.title) + '</div>' +
          '<div class="muted small">' + esc(t.phase || '') + ' · ' + esc(t.progressText || '') + '</div>' +
        '</div>' +
        '<div class="spacer"></div>' +
        '<span class="badge ' + cls + '">' + esc(t.statusText || t.status) + '</span>' +
        ((t.status === 'Running' || t.status === 'Pending') ? '<button class="btn sm danger" data-act="task-cancel" data-id="' + esc(t.id) + '">取消</button>' : '') +
        '</div>';
    });
    html += '</div>';
    box.innerHTML = html;
  }

  async function refreshTaskLogs(id) {
    try {
      var d = await api('/api/task?id=' + encodeURIComponent(id));
      if (d.task && d.task.logs) {
        state.taskLogs[id] = d.task.logs;
        var details = document.querySelector('details.logs[data-id="' + cssEsc(id) + '"]');
        if (details) {
          var pre = details.querySelector('.log-pre');
          if (pre) pre.textContent = d.task.logs.length ? d.task.logs.join('\n') : '（暂无日志）';
          var summary = details.querySelector('summary');
          if (summary) summary.textContent = logSummaryText(d.task.logCount, d.task.logs.length);
        }
      }
    } catch (e) { }
  }

  async function cancelTask(id) {
    if (!id) return;
    try {
      await api('/api/cancel?id=' + encodeURIComponent(id));
      toast('已请求取消任务', 'success');
      refreshTasks();
    } catch (e) { toast('取消失败：' + e.message, 'error'); }
  }

  // ---------- 安装 ----------
  async function doInstall(kind, args, label) {
    try {
      var d = await api('/api/install', { method: 'POST', body: { kind: kind, args: args } });
      toast('已创建任务：' + (d.title || label || kind), 'success', '任务已开始');
      refreshTasks();
      showView('tasks');
    } catch (e) {
      toast('创建安装任务失败：' + e.message, 'error');
    }
  }

  async function doPublish() {
    try {
      await api('/api/homepage?action=publish');
      toast('已重写 PCL 主页文件。切回 PCL 主界面，点击『刷新主页』即可看到最新状态。', 'success', '主页已刷新');
    } catch (e) {
      toast('重写主页失败：' + e.message, 'error');
    }
  }

  // ---------- 首页 ----------
  async function loadHome() {
    loadStatus();
    loadInstalled();
    ensureManifestSummary();
    renderHomeTasks();
  }

  async function ensureManifestSummary() {
    var m = state.manifest;
    if (m.summary) { applyManifestSummary(m.summary); return; }
    try {
      var d = await api('/api/manifest?filter=release&page=1&pageSize=1');
      m.summary = d;
      applyManifestSummary(d);
    } catch (e) { /* 静默：首页按钮保持禁用 */ }
  }

  function applyManifestSummary(d) {
    state.latestRelease = d.latestRelease || state.latestRelease;
    state.latestSnapshot = d.latestSnapshot || state.latestSnapshot;
    var rel = $('#homeInstallRelease');
    if (rel) {
      rel.disabled = !state.latestRelease;
      rel.textContent = state.latestRelease ? ('安装最新正式版 ' + state.latestRelease) : '安装最新正式版';
    }
    var snap = $('#homeInstallSnapshot');
    if (snap) {
      snap.disabled = !state.latestSnapshot;
      snap.textContent = state.latestSnapshot ? ('安装最新快照 ' + state.latestSnapshot) : '安装最新快照';
    }
    if (d.warning) toast(d.warning, 'warn', '版本清单');
  }

  async function loadInstalled() {
    try {
      var d = await api('/api/installed');
      state.installed = d.versions || [];
      renderInstalled();
    } catch (e) {
      var box = $('#installedList');
      if (box) box.innerHTML = errorHtml('读取已安装版本失败：' + e.message, 'reload-installed');
    }
  }

  function renderInstalled() {
    var box = $('#installedList');
    if (!box) return;
    var list = state.installed || [];
    var summary = $('#installedSummary');
    if (summary) summary.textContent = list.length ? ('共 ' + list.length + ' 个版本') : '';
    var si = $('#statInstalled');
    if (si) si.textContent = list.length;
    if (!list.length) { box.innerHTML = emptyHtml('📦', '还没有已安装的版本。'); return; }
    var html = '<div class="chips">';
    list.forEach(function (v) {
      var tip = v.name + (v.inheritsFrom ? '（继承 ' + v.inheritsFrom + '）' : '') + (v.modified ? ' · ' + v.modified : '');
      html += '<span class="chip installed" title="' + esc(tip) + '">' + esc(v.name) + '</span>';
    });
    html += '</div>';
    box.innerHTML = html;
  }

  // ---------- 版本清单 ----------
  function manifestKey() {
    var m = state.manifest;
    return m.filter + '|' + m.query + '|' + m.page + '|' + m.pageSize;
  }

  async function loadVanilla(force) {
    var m = state.manifest;
    if (!force && m.data && m.dataKey === manifestKey()) { renderVanilla(); return; }
    var key = manifestKey();
    var body = $('#vanillaBody');
    if (body) body.innerHTML = '<div class="loading"><span class="spinner"></span>正在获取版本清单…</div>';
    try {
      var qs = 'filter=' + encodeURIComponent(m.filter) +
        '&page=' + m.page +
        '&pageSize=' + m.pageSize +
        '&query=' + encodeURIComponent(m.query) +
        (force ? '&refresh=1' : '');
      var d = await api('/api/manifest?' + qs);
      m.data = d;
      m.dataKey = key;
      if (d.latestRelease) state.latestRelease = d.latestRelease;
      if (d.latestSnapshot) state.latestSnapshot = d.latestSnapshot;
      if (d.warning) toast(d.warning, 'warn', '版本清单');
      if (key === manifestKey()) renderVanilla();
    } catch (e) {
      var box = $('#vanillaBody');
      if (box) box.innerHTML = errorHtml('获取版本清单失败：' + e.message, 'vanilla-retry');
    }
  }

  function typeBadge(type) {
    type = type || '';
    if (type === 'release') return '<span class="badge blue">正式版</span>';
    if (type === 'snapshot') return '<span class="badge orange">快照</span>';
    if (type === 'old_beta') return '<span class="badge gray">远古 Beta</span>';
    if (type === 'old_alpha') return '<span class="badge gray">远古 Alpha</span>';
    return '<span class="badge gray">' + esc(type) + '</span>';
  }

  function renderVanilla() {
    var m = state.manifest, d = m.data, box = $('#vanillaBody');
    if (!box || !d) return;
    var versions = d.versions || [];
    var summary = $('#vanillaSummary');
    if (summary) summary.textContent = '共 ' + d.total + ' 个版本' + (d.query ? '，搜索「' + d.query + '」' : '');
    var info = $('#vanillaPageInfo');
    if (info) info.textContent = '第 ' + d.page + ' / ' + d.pages + ' 页';
    var prev = $('#vanillaPrev'), next = $('#vanillaNext');
    if (prev) prev.disabled = d.page <= 1;
    if (next) next.disabled = d.page >= d.pages;
    if (!versions.length) { box.innerHTML = emptyHtml('🔍', '没有匹配的版本'); return; }
    var html = '<div class="table-wrap"><table class="table"><thead><tr>' +
      '<th>版本号</th><th>类型</th><th>发布时间</th><th class="op">操作</th>' +
      '</tr></thead><tbody>';
    versions.forEach(function (v) {
      html += '<tr>' +
        '<td class="ver">' + esc(v.id) + '</td>' +
        '<td>' + typeBadge(v.type) + '</td>' +
        '<td class="muted">' + esc(v.releaseTime || '') + '</td>' +
        '<td class="op"><button class="btn sm primary" data-act="install-vanilla" data-id="' + esc(v.id) + '">安装</button></td>' +
        '</tr>';
    });
    html += '</tbody></table></div>';
    box.innerHTML = html;
  }

  // ---------- 加载器 ----------
  async function ensureReleaseVersions() {
    if (state.releaseVersions && state.releaseVersions.length) { fillLoaderMc(); return; }
    try {
      var all = await fetchAllManifest('release');
      state.releaseVersions = all;
      fillLoaderMc();
      fillPackGameVersions();
    } catch (e) {
      var sel = $('#loaderMc');
      if (sel) sel.innerHTML = '<option value="">MC 版本加载失败</option>';
      toast('加载 MC 版本列表失败：' + e.message, 'error');
    }
  }

  async function fetchAllManifest(filter) {
    var out = [], page = 1, pages = 1, guard = 0;
    while (page <= pages && guard < 40) {
      guard++;
      var d = await api('/api/manifest?filter=' + encodeURIComponent(filter) + '&page=' + page + '&pageSize=1000');
      out = out.concat(d.versions || []);
      pages = d.pages || 1;
      if (d.latestRelease) state.latestRelease = d.latestRelease;
      if (d.latestSnapshot) state.latestSnapshot = d.latestSnapshot;
      page++;
    }
    return out;
  }

  function fillLoaderMc() {
    var sel = $('#loaderMc');
    if (!sel || sel.dataset.filled === '1') return;
    var list = state.releaseVersions || [];
    if (!list.length) { sel.innerHTML = '<option value="">没有可用的 MC 版本</option>'; return; }
    var latest = state.latestRelease || list[0].id;
    var html = '';
    list.forEach(function (v) {
      html += '<option value="' + esc(v.id) + '"' + (v.id === latest ? ' selected' : '') + '>' + esc(v.id) + '</option>';
    });
    sel.innerHTML = html;
    sel.dataset.filled = '1';
  }

  function fillPackGameVersions() {
    var sel = $('#packGameVersion');
    if (!sel || sel.dataset.filled === '1') return;
    var list = (state.releaseVersions || []).slice(0, 80);
    var html = '<option value="">全部游戏版本</option>';
    list.forEach(function (v) { html += '<option value="' + esc(v.id) + '">' + esc(v.id) + '</option>'; });
    sel.innerHTML = html;
    sel.dataset.filled = '1';
  }

  async function loadLoaderPage() {
    await ensureReleaseVersions();
    fillLoaderMc();
  }

  function clearLoaderList() {
    var box = $('#loaderList');
    if (box) box.innerHTML = emptyHtml('🧩', '点击「获取版本列表」查看可安装的加载器版本。');
    var hint = $('#loaderHint');
    if (hint) hint.innerHTML = '';
  }

  async function loadLoaderVersions() {
    var loaderEl = $('#loaderKind'), mcEl = $('#loaderMc');
    var loader = loaderEl ? loaderEl.value : '';
    var mc = mcEl ? mcEl.value : '';
    var listBox = $('#loaderList'), hintBox = $('#loaderHint');
    if (hintBox) hintBox.innerHTML = '';
    if (!mc) {
      if (hintBox) hintBox.innerHTML = '<div class="hint warn">请先选择 Minecraft 版本。</div>';
      return;
    }
    state.loader.loading = true;
    var btn = $('#loaderLoadBtn');
    if (btn) { btn.disabled = true; btn.classList.add('spinning'); }
    if (listBox) listBox.innerHTML = '<div class="loading"><span class="spinner"></span>正在获取 ' + esc(loader) + ' 的版本列表…</div>';
    try {
      var d = await api('/api/loader/versions?loader=' + encodeURIComponent(loader) + '&mc=' + encodeURIComponent(mc));
      state.loader.versions = d.versions || [];
      state.loader.error = '';
      renderLoaderVersions(loader, mc, d);
    } catch (e) {
      state.loader.versions = null;
      state.loader.error = e.message;
      var ready = !!(e.data && e.data.ready === false);
      if (hintBox) hintBox.innerHTML = '<div class="hint warn">' + esc(e.message) + (ready ? '（该模块仍在开发中，完成后即可使用）' : '') + '</div>';
      if (listBox) listBox.innerHTML = emptyHtml('🧩', ready ? '加载器模块尚未完成，暂时无法列出可安装版本。' : '获取版本列表失败。');
    } finally {
      state.loader.loading = false;
      if (btn) { btn.disabled = false; btn.classList.remove('spinning'); }
    }
  }

  function renderLoaderVersions(loader, mc, d) {
    var box = $('#loaderList');
    if (!box) return;
    var versions = d.versions || [];
    if (!versions.length) {
      box.innerHTML = emptyHtml('🧩', '没有找到 ' + mc + ' 可用的 ' + loader + ' 版本');
      return;
    }
    var html = '<div class="list scroll">';
    versions.forEach(function (v) {
      html += '<div class="list-row">' +
        '<span class="name">' + esc(v.version) + '</span>' +
        (v.isRecommended ? '<span class="badge green">推荐</span>' : '') +
        ((v.isLatest && !v.isRecommended) ? '<span class="badge blue">最新</span>' : '') +
        '<span class="muted small">' + esc(v.display || v.version) + '</span>' +
        '<div class="spacer"></div>' +
        '<button class="btn sm primary" data-act="install-loader" data-loader="' + esc(loader) + '" data-mc="' + esc(mc) + '" data-ver="' + esc(v.version) + '">安装</button>' +
        '</div>';
    });
    html += '</div>';
    box.innerHTML = html;
  }

  // ---------- 整合包 ----------
  async function loadPackPage() {
    try { await ensureReleaseVersions(); } catch (e) { }
    fillPackGameVersions();
    if (!state.pack.queried) searchPacks(true);
  }

  async function searchPacks(reset) {
    var p = state.pack;
    if (reset) p.offset = 0;
    var queryEl = $('#packQuery');
    if (queryEl) p.query = queryEl.value.trim();
    var gv = $('#packGameVersion');
    if (gv) p.gameVersion = gv.value;
    var ld = $('#packLoader');
    if (ld) p.loader = ld.value;
    p.loading = true;
    var box = $('#packResults');
    if (box) box.innerHTML = '<div class="loading"><span class="spinner"></span>正在搜索 Modrinth…</div>';
    try {
      var qs = 'query=' + encodeURIComponent(p.query) +
        '&projectType=modpack&offset=' + p.offset + '&limit=' + p.limit +
        (p.gameVersion ? '&gameVersion=' + encodeURIComponent(p.gameVersion) : '') +
        (p.loader ? '&loader=' + encodeURIComponent(p.loader) : '');
      var d = await api('/api/modrinth/search?' + qs);
      p.hits = d.hits || [];
      p.total = d.totalHits || 0;
      p.error = '';
      p.queried = true;
      renderPacks();
    } catch (e) {
      p.error = e.message;
      p.hits = [];
      p.total = 0;
      if (box) box.innerHTML = errorHtml('搜索失败：' + e.message, 'pack-search');
    } finally {
      p.loading = false;
    }
  }

  function renderPacks() {
    var p = state.pack, box = $('#packResults');
    if (!box) return;
    var info = $('#packPageInfo');
    if (info) {
      var totalPages = p.total ? Math.ceil(p.total / p.limit) : 1;
      var page = Math.floor(p.offset / p.limit) + 1;
      info.textContent = p.total ? ('第 ' + page + ' / ' + totalPages + ' 页，共 ' + fmtNum(p.total) + ' 个') : '-';
    }
    if (!p.hits.length) {
      box.innerHTML = emptyHtml('📦', p.query ? ('没有找到与「' + p.query + '」匹配的整合包') : '输入关键词搜索 Modrinth 整合包');
      return;
    }
    var html = '<div class="pack-grid">';
    p.hits.forEach(function (h) {
      var icon = h.iconUrl
        ? '<img class="pack-icon" src="' + esc(h.iconUrl) + '" alt="" loading="lazy" onerror="this.remove()">'
        : '<div class="pack-icon placeholder">📦</div>';
      var link = 'https://modrinth.com/modpack/' + encodeURIComponent(h.slug || h.projectId || '');
      var cats = (h.categories || []).slice(0, 3).map(function (c) {
        return '<span class="badge gray">' + esc(c) + '</span>';
      }).join('');
      html += '<div class="pack-card">' + icon +
        '<div class="pack-main">' +
          '<div class="pack-title" title="' + esc(h.title) + '">' + esc(h.title) + '</div>' +
          '<div class="pack-desc">' + esc(h.description || '') + '</div>' +
          '<div class="pack-meta">' +
            '<span>⬇ ' + fmtNum(h.downloads) + '</span>' +
            '<span>❤ ' + fmtNum(h.follows) + '</span>' +
            (h.author ? '<span>@' + esc(h.author) + '</span>' : '') +
            cats +
          '</div>' +
          '<div class="pack-actions">' +
            '<button class="btn sm primary" data-act="install-pack" data-id="' + esc(h.projectId || h.slug) + '">安装</button>' +
            '<a class="btn sm" href="' + esc(link) + '" target="_blank" rel="noreferrer">详情</a>' +
          '</div>' +
        '</div>' +
      '</div>';
    });
    html += '</div>';
    box.innerHTML = html;
  }

  function extractModrinthId(raw) {
    var m = String(raw).match(/modrinth\.com\/(?:modpack|mod|project|datapack|resourcepack|shader)\/([A-Za-z0-9_\-]+)/i);
    if (m) return m[1];
    var parts = String(raw).replace(/[?#].*$/, '').split('/');
    return parts[parts.length - 1] || '';
  }

  async function installPackCf() {
    var el = $('#packCfInput');
    var raw = el ? el.value.trim() : '';
    if (!raw) { toast('请输入 CurseForge 整合包 ID', 'warn'); return; }
    await doInstall('modpack', ['curseforge', raw], 'CurseForge 整合包');
  }

  async function installPackById() {
    var el = $('#packIdInput');
    var raw = el ? el.value.trim() : '';
    if (!raw) { toast('请输入 Modrinth 整合包 ID 或链接', 'warn'); return; }
    var verEl = $('#packVersionInput');
    var ver = verEl ? verEl.value.trim() : '';
    var isUrl = /^https?:\/\//i.test(raw);
    if (isUrl && raw.indexOf('modrinth.com') < 0) {
      var urlArgs = ['url', raw];
      if (ver) urlArgs.push(ver);
      await doInstall('modpack', urlArgs, '整合包');
      return;
    }
    var id = isUrl ? extractModrinthId(raw) : raw;
    if (!id) { toast('无法从链接中识别项目 ID', 'error'); return; }
    var args = ['modrinth', id];
    if (ver) args.push(ver);
    await doInstall('modpack', args, '整合包');
  }

  // ---------- 设置 ----------
  async function loadSettings() {
    try {
      var d = await api('/api/config/save');
      state.config = d.config;
      var c = d.config || {};
      var set = function (id, value) { var el = $('#' + id); if (el) el.value = value; };
      var check = function (id, value) { var el = $('#' + id); if (el) el.checked = !!value; };
      set('setDownloadSource', c.downloadSource || 'official');
      set('setThreads', c.threads || 32);
      set('setServerPort', c.serverPort || 27321);
      set('setMaxConcurrentTasks', c.maxConcurrentTasks || 1);
      set('setMcDir', c.mcDir || '');
      set('setJavaPath', c.javaPath || '');
      set('setCfKey', c.curseForgeApiKey || '');
      set('setHomepageMaxVersions', c.homepageMaxVersions || 30);
      check('setHomepagePublish', c.homepagePublish);
      check('setHomepageShowSnapshots', c.homepageShowSnapshots);
      check('setUseMirrorFallback', c.useMirrorFallback);
      var hint = $('#settingsHint');
      if (hint) hint.innerHTML = '<div class="hint">配置文件：<span class="code">' + esc(c.configFile || '') + '</span>；保存后写入该文件。</div>';
    } catch (e) {
      toast('读取设置失败：' + e.message, 'error');
    }
  }

  async function saveSettings() {
    var btn = $('#saveConfigBtn');
    busy(btn, true, '保存中…');
    try {
      var num = function (id, fallback) {
        var el = $('#' + id);
        var v = el ? Number(el.value) : fallback;
        return isNaN(v) ? fallback : v;
      };
      var val = function (id) { var el = $('#' + id); return el ? el.value.trim() : ''; };
      var chk = function (id) { var el = $('#' + id); return el ? !!el.checked : false; };
      var payload = {
        downloadSource: (function () { var el = $('#setDownloadSource'); return el ? el.value : 'official'; })(),
        threads: num('setThreads', 32),
        serverPort: num('setServerPort', 27321),
        maxConcurrentTasks: num('setMaxConcurrentTasks', 1),
        mcDir: val('setMcDir'),
        javaPath: val('setJavaPath'),
        curseForgeApiKey: val('setCfKey'),
        homepageMaxVersions: num('setHomepageMaxVersions', 30),
        homepagePublish: chk('setHomepagePublish'),
        homepageShowSnapshots: chk('setHomepageShowSnapshots'),
        useMirrorFallback: chk('setUseMirrorFallback')
      };
      var d = await api('/api/config/save', { method: 'POST', body: payload });
      state.config = d.config;
      var hint = $('#settingsHint');
      if (hint) hint.innerHTML = '<div class="hint">' + esc(d.note || '设置已保存。') + '</div>';
      toast(d.note || '设置已保存', d.note ? 'warn' : 'success');
      loadStatus();
    } catch (e) {
      toast('保存失败：' + e.message, 'error');
    } finally {
      busy(btn, false);
    }
  }

  // ---------- 关于 / 日志 ----------
  var globalLogLines = [];

  function appendGlobalLog(line) {
    if (!line) return;
    globalLogLines.push(String(line));
    while (globalLogLines.length > 500) globalLogLines.shift();
    if (state.view === 'about') renderLogPre();
  }

  function renderLogPre() {
    var pre = $('#logPre');
    if (!pre) return;
    pre.textContent = globalLogLines.length ? globalLogLines.join('\n') : '（暂无日志）';
    pre.scrollTop = pre.scrollHeight;
  }

  async function loadLogsQuiet() {
    try {
      var d = await api('/api/logs?lines=150');
      globalLogLines = (d.lines || []).slice();
      renderLogPre();
      var ft = $('#logFileText');
      if (ft) ft.textContent = d.file || '';
    } catch (e) { }
  }

  async function loadAbout() {
    try {
      var d = await api('/api/about');
      state.about = d;
      var p = d.paths || {};
      var summary = $('#aboutSummary');
      if (summary) summary.textContent = 'v' + d.version + ' · 已运行 ' + fmtDuration(d.uptimeSeconds);
      var copyBtn = function (text) {
        return ' <button class="btn xs" data-act="copy" data-copy="' + esc(text) + '">复制</button>';
      };
      var row = function (k, v) { return '<div class="k">' + esc(k) + '</div><div class="v">' + v + '</div>'; };
      var html = '<div class="kv">' +
        row('版本', esc(d.version)) +
        row('服务地址', esc(d.url)) +
        row('运行时长', esc(fmtDuration(d.uptimeSeconds))) +
        row('已安装版本', (d.installedCount || 0) + ' 个') +
        row('下载源', esc(d.config ? (d.config.downloadSource === 'bmclapi' ? 'BMCLAPI 镜像' : '官方源') : '')) +
        row('Minecraft 目录', esc(p.mcDir) + copyBtn(p.mcDir)) +
        row('PCL 目录', esc(p.pclDir) + copyBtn(p.pclDir)) +
        row('插件目录', esc(p.pluginDir) + copyBtn(p.pluginDir)) +
        row('配置文件', esc(p.configFile) + copyBtn(p.configFile)) +
        row('主页文件', esc(p.homepageFile) + copyBtn(p.homepageFile)) +
        row('日志文件', esc(p.logFile) + copyBtn(p.logFile)) +
        '</div>';
      var info = $('#aboutInfo');
      if (info) info.innerHTML = html;
      var ft = $('#logFileText');
      if (ft) ft.textContent = p.logFile || '';
      loadLogsQuiet();
    } catch (e) {
      var info = $('#aboutInfo');
      if (info) info.innerHTML = errorHtml('读取运行信息失败：' + e.message, 'reload-about');
    }
  }

  // ---------- 事件绑定 ----------
  document.addEventListener('click', function (ev) {
    var el = ev.target && ev.target.closest ? ev.target.closest('[data-act]') : null;
    if (!el) return;
    var act = el.getAttribute('data-act');
    switch (act) {
      case 'goto':
        ev.preventDefault();
        showView(el.getAttribute('data-view'));
        break;
      case 'publish': doPublish(); break;
      case 'reload-installed': loadInstalled(); break;
      case 'reload-about': loadAbout(); break;
      case 'reload-config': loadSettings(); break;
      case 'reload-logs': loadLogsQuiet(); break;
      case 'home-install-release':
        doInstall('vanilla', [state.latestRelease || 'latest'], '原版最新正式版');
        break;
      case 'home-install-snapshot':
        doInstall('vanilla', [state.latestSnapshot || 'snapshot'], '原版最新快照');
        break;
      case 'install-vanilla': doInstall('vanilla', [el.getAttribute('data-id')], '原版'); break;
      case 'install-loader':
        doInstall('loader', [el.getAttribute('data-loader'), el.getAttribute('data-mc'), el.getAttribute('data-ver')], '加载器');
        break;
      case 'install-pack': doInstall('modpack', ['modrinth', el.getAttribute('data-id')], '整合包'); break;
      case 'pack-search': searchPacks(true); break;
      case 'pack-install-id': installPackById(); break;
      case 'pack-install-cf': installPackCf(); break;
      case 'pack-prev':
        if (state.pack.offset > 0) { state.pack.offset = Math.max(0, state.pack.offset - state.pack.limit); searchPacks(false); }
        break;
      case 'pack-next':
        if (state.pack.offset + state.pack.limit < state.pack.total) { state.pack.offset += state.pack.limit; searchPacks(false); }
        break;
      case 'vanilla-filter': {
        var seg = $('#vanillaFilter');
        if (seg) $$('.seg-btn', seg).forEach(function (b) { b.classList.remove('active'); });
        el.classList.add('active');
        state.manifest.filter = el.getAttribute('data-filter') || 'release';
        state.manifest.page = 1;
        loadVanilla();
        break;
      }
      case 'vanilla-prev':
        if (state.manifest.page > 1) { state.manifest.page--; loadVanilla(); }
        break;
      case 'vanilla-next':
        if (state.manifest.data && state.manifest.page < state.manifest.data.pages) { state.manifest.page++; loadVanilla(); }
        break;
      case 'vanilla-refresh': loadVanilla(true); break;
      case 'vanilla-retry': loadVanilla(); break;
      case 'loader-load': loadLoaderVersions(); break;
      case 'tasks-refresh': refreshTasks(); break;
      case 'task-cancel': cancelTask(el.getAttribute('data-id')); break;
      case 'save-config': saveSettings(); break;
      case 'copy': copyText(el.getAttribute('data-copy')); break;
      default: break;
    }
  });

  // 日志折叠展开：打开时按需拉取该任务的完整日志
  document.addEventListener('toggle', function (ev) {
    var el = ev.target;
    if (!el || !el.classList || !el.classList.contains('logs')) return;
    var id = el.getAttribute('data-id');
    if (!id) return;
    state.openLogs[id] = !!el.open;
    if (el.open) refreshTaskLogs(id);
  }, true);

  function bindStatic() {
    var search = $('#vanillaSearch');
    if (search) search.addEventListener('input', debounce(function () {
      state.manifest.query = search.value.trim();
      state.manifest.page = 1;
      loadVanilla();
    }, 350));

    var pageSize = $('#vanillaPageSize');
    if (pageSize) pageSize.addEventListener('change', function () {
      state.manifest.pageSize = parseInt(pageSize.value, 10) || 50;
      state.manifest.page = 1;
      loadVanilla();
    });

    var loaderKind = $('#loaderKind');
    if (loaderKind) loaderKind.addEventListener('change', function () { clearLoaderList(); });
    var loaderMc = $('#loaderMc');
    if (loaderMc) loaderMc.addEventListener('change', function () { clearLoaderList(); });

    var packQuery = $('#packQuery');
    if (packQuery) packQuery.addEventListener('keydown', function (ev) {
      if (ev.key === 'Enter') searchPacks(true);
    });
    var packId = $('#packIdInput');
    if (packId) packId.addEventListener('keydown', function (ev) {
      if (ev.key === 'Enter') installPackById();
    });
    var packCf = $('#packCfInput');
    if (packCf) packCf.addEventListener('keydown', function (ev) {
      if (ev.key === 'Enter') installPackCf();
    });

    var mcCard = $('#statMcCard');
    if (mcCard) mcCard.addEventListener('click', function () {
      var el = $('#statMc');
      copyText(el ? el.textContent : '');
    });

    window.addEventListener('hashchange', function () { showView(viewFromHash()); });
  }

  function init() {
    bindStatic();
    connectSse();
    showView(viewFromHash());
    // 看门狗：SSE 长时间没有事件时退回轮询，避免界面卡住
    setInterval(function () {
      if (!state.sseOk) return;
      if (Date.now() - state.lastSseAt > 16000) startPolling();
    }, 6000);
    // 若轮询兜底期间 SSE 恢复，则停掉轮询
    setInterval(function () {
      if (state.sseOk && state.pollTimer) stopPolling();
    }, 8000);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
""";
    }
}