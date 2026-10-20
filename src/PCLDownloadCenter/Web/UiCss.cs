// Web UI 样式（内嵌到程序集，不依赖任何外部 CDN / 字体）
namespace PCLDownloadCenter
{
    internal static class UiCss
    {
        public const string Text = """
:root{
  --blue:#0e63c8; --blue-2:#1a7be0; --blue-dark:#0b4f9e;
  --bg:#eef2f7; --card:#ffffff; --text:#1f2937; --muted:#64748b;
  --border:#dbe3ec; --line:#eef2f7;
  --green:#16a34a; --green-bg:#e8f7ee;
  --red:#dc2626; --red-bg:#fdecec;
  --orange:#d97706; --orange-bg:#fdf3e3;
  --purple:#7c3aed; --shadow:0 2px 10px rgba(15,42,74,.06);
}
*{box-sizing:border-box}
html,body{margin:0;padding:0}
body{
  background:var(--bg); color:var(--text);
  font-family:"Segoe UI","Microsoft YaHei","PingFang SC","Hiragino Sans GB",system-ui,-apple-system,sans-serif;
  font-size:14px; line-height:1.55; -webkit-font-smoothing:antialiased;
}
button,input,select,textarea{font-family:inherit}
a{color:var(--blue);text-decoration:none}

/* ---------- 顶栏 ---------- */
.topbar{
  position:sticky; top:0; z-index:30;
  background:linear-gradient(135deg,#0d5cb8 0%,#1273d4 55%,#2b8ae4 100%);
  color:#fff; box-shadow:0 2px 12px rgba(9,53,102,.22);
}
.topbar-row{
  max-width:1180px; margin:0 auto; padding:10px 16px 8px;
  display:flex; align-items:center; gap:12px; flex-wrap:wrap;
}
.brand{display:flex;align-items:center;gap:10px;min-width:0}
.logo{
  width:38px;height:38px;border-radius:10px;flex:0 0 auto;
  background:#fff;color:var(--blue);font-weight:800;font-size:14px;
  display:flex;align-items:center;justify-content:center;letter-spacing:.5px;
  box-shadow:0 2px 6px rgba(0,0,0,.16);
}
.brand-title{font-size:17px;font-weight:700;line-height:1.2;white-space:nowrap}
.brand-sub{font-size:12px;opacity:.86;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.topbar-actions{margin-left:auto;display:flex;align-items:center;gap:8px;flex-wrap:wrap}
.status-pill,.metric-pill{
  display:inline-flex;align-items:center;gap:6px;
  background:rgba(255,255,255,.16); border:1px solid rgba(255,255,255,.26);
  border-radius:999px; padding:4px 11px; font-size:12px; white-space:nowrap;
}
.dot{width:8px;height:8px;border-radius:50%;background:#fbbf24;box-shadow:0 0 0 2px rgba(251,191,36,.25)}
.dot.ok{background:#4ade80;box-shadow:0 0 0 2px rgba(74,222,128,.3)}
.dot.bad{background:#f87171;box-shadow:0 0 0 2px rgba(248,113,113,.3)}
.metric-num{font-weight:700}
.metric-sep{opacity:.5}

/* ---------- 导航 ---------- */
.tabs{
  max-width:1180px; margin:0 auto; padding:0 10px 8px;
  display:flex; gap:4px; overflow-x:auto; scrollbar-width:none;
}
.tabs::-webkit-scrollbar{display:none}
.tab{
  appearance:none;border:0;background:transparent;color:rgba(255,255,255,.85);
  padding:7px 14px;border-radius:9px;cursor:pointer;font-size:13.5px;white-space:nowrap;
  display:inline-flex;align-items:center;gap:6px;
}
.tab:hover{background:rgba(255,255,255,.13);color:#fff}
.tab.active{background:#fff;color:var(--blue);font-weight:600;box-shadow:0 2px 6px rgba(0,0,0,.12)}
.tab-badge{
  background:var(--red);color:#fff;border-radius:999px;font-size:11px;
  min-width:17px;height:17px;line-height:17px;text-align:center;padding:0 4px;font-weight:700;
}
.tab.active .tab-badge{background:var(--blue)}

/* ---------- 主体 ---------- */
.container{max-width:1180px;margin:0 auto;padding:16px 16px 56px}
.view{display:none}
.view.active{display:block;animation:fade .18s ease-in}
@keyframes fade{from{opacity:0;transform:translateY(3px)}to{opacity:1;transform:none}}

.card{
  background:var(--card); border:1px solid var(--border); border-radius:13px;
  box-shadow:var(--shadow); padding:16px 18px; margin-bottom:14px;
}
.card-head{display:flex;align-items:center;gap:10px;margin-bottom:12px;flex-wrap:wrap}
.card-head h2{margin:0;font-size:16px;font-weight:700}
.card-head .spacer{margin-left:auto}
.muted{color:var(--muted);font-size:12.5px}
.small{font-size:12px}
.mono{font-family:Consolas,"Courier New",monospace}

/* ---------- 按钮 / 输入 ---------- */
.btn{
  appearance:none;border:1px solid var(--border);background:#fff;color:var(--text);
  padding:7px 14px;border-radius:9px;font-size:13.5px;cursor:pointer;
  display:inline-flex;align-items:center;justify-content:center;gap:6px;
  transition:background .15s,border-color .15s,color .15s,box-shadow .15s;
  white-space:nowrap;
}
.btn:hover{background:#f4f8fd;border-color:#b9cde4}
.btn:active{transform:translateY(1px)}
.btn.primary{background:var(--blue);border-color:var(--blue);color:#fff;font-weight:600}
.btn.primary:hover{background:var(--blue-2);border-color:var(--blue-2)}
.btn.danger{background:#fff;border-color:#f2c4c4;color:var(--red)}
.btn.danger:hover{background:var(--red-bg)}
.btn.ghost{background:rgba(255,255,255,.14);border-color:rgba(255,255,255,.3);color:#fff}
.btn.ghost:hover{background:rgba(255,255,255,.24)}
.btn.sm{padding:5px 10px;font-size:12.5px;border-radius:8px}
.btn.xs{padding:3px 9px;font-size:12px;border-radius:7px}
.btn:disabled{opacity:.55;cursor:not-allowed;transform:none}
.btn.spinning::before{
  content:"";width:12px;height:12px;border-radius:50%;
  border:2px solid rgba(255,255,255,.4);border-top-color:#fff;
  animation:spin .7s linear infinite;
}
.btn.ghost.spinning::before,.btn:not(.primary).spinning::before{
  border-color:#c7d7ea;border-top-color:var(--blue);
}
@keyframes spin{to{transform:rotate(360deg)}}

.input,.select{
  background:#fff;color:var(--text);border:1px solid #c6d3e2;border-radius:9px;
  padding:7px 11px;font-size:13.5px;outline:none;min-width:0;
}
.input::placeholder{color:#94a3b8}
.input:focus,.select:focus{border-color:var(--blue);box-shadow:0 0 0 3px rgba(14,99,200,.14)}
.input.grow{flex:1 1 220px}
.select.sm,.input.sm{padding:5px 9px;font-size:12.5px;border-radius:8px}
.input[readonly]{background:#f6f8fb;color:#475569}

.toolbar{display:flex;align-items:center;gap:9px;flex-wrap:wrap;margin-bottom:12px}
.toolbar .spacer{margin-left:auto}

.seg{display:inline-flex;background:#eef2f7;border:1px solid var(--border);border-radius:10px;padding:2px}
.seg-btn{
  appearance:none;border:0;background:transparent;color:var(--muted);
  padding:5px 12px;border-radius:8px;font-size:13px;cursor:pointer;white-space:nowrap;
}
.seg-btn:hover{color:var(--text)}
.seg-btn.active{background:#fff;color:var(--blue);font-weight:600;box-shadow:0 1px 4px rgba(15,42,74,.12)}

/* ---------- 首页 ---------- */
.hero{
  background:linear-gradient(135deg,#0e63c8,#2b8ae4 70%,#46a2f2);
  color:#fff;border:0;border-radius:15px;padding:22px 24px;margin-bottom:14px;
  box-shadow:0 6px 20px rgba(14,99,200,.24);
}
.hero h1{margin:0 0 6px;font-size:22px}
.hero p{margin:0 0 14px;color:rgba(255,255,255,.9);font-size:13.5px}
.hero-actions{display:flex;gap:9px;flex-wrap:wrap}
.hero-actions .btn{background:#fff;border-color:#fff;color:var(--blue);font-weight:600}
.hero-actions .btn:hover{background:#eaf3ff}
.hero-actions .btn.ghost{background:rgba(255,255,255,.16);border-color:rgba(255,255,255,.4);color:#fff}
.hero-actions .btn.ghost:hover{background:rgba(255,255,255,.26)}
.stats{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-bottom:14px}
.stat{padding:14px 16px;margin:0}
.stat-num{font-size:21px;font-weight:800;color:var(--blue);line-height:1.25;word-break:break-all}
.stat-label{font-size:12px;color:var(--muted);margin-top:2px}
.stat.clickable{cursor:pointer}
.stat.clickable:hover{border-color:#b9cde4}

/* ---------- 表格 ---------- */
.table-wrap{overflow-x:auto;border:1px solid var(--line);border-radius:10px}
.table{width:100%;border-collapse:collapse;font-size:13.5px;min-width:560px}
.table th{
  text-align:left;padding:9px 12px;background:#f7f9fc;color:var(--muted);
  font-weight:600;font-size:12.5px;border-bottom:1px solid var(--line);white-space:nowrap;
}
.table td{padding:8px 12px;border-bottom:1px solid var(--line);vertical-align:middle}
.table tbody tr:last-child td{border-bottom:0}
.table tbody tr:hover{background:#f8fbff}
.table .op{text-align:right;width:110px;white-space:nowrap}
.table .ver{font-weight:600;font-family:Consolas,"Courier New",monospace;font-size:13px}

/* ---------- 徽标 ---------- */
.badge{
  display:inline-block;padding:1px 8px;border-radius:999px;font-size:11.5px;
  font-weight:600;line-height:1.7;white-space:nowrap;
}
.badge.blue{background:#e7f0fc;color:var(--blue)}
.badge.green{background:var(--green-bg);color:var(--green)}
.badge.red{background:var(--red-bg);color:var(--red)}
.badge.orange{background:var(--orange-bg);color:var(--orange)}
.badge.purple{background:#f1ebfd;color:var(--purple)}
.badge.gray{background:#eef2f7;color:var(--muted)}

/* ---------- 列表 ---------- */
.list{display:flex;flex-direction:column;gap:8px}
.list.scroll{max-height:540px;overflow-y:auto;padding-right:4px}
.list-row{
  display:flex;align-items:center;gap:10px;padding:9px 12px;
  border:1px solid var(--line);border-radius:10px;background:#fff;flex-wrap:wrap;
}
.list-row:hover{border-color:#cfdded;background:#fbfdff}
.list-row .name{font-weight:600;font-family:Consolas,"Courier New",monospace}
.list-row .spacer{margin-left:auto}
.chips{display:flex;flex-wrap:wrap;gap:7px}
.chip{
  background:#f3f7fc;border:1px solid var(--border);color:#33506e;
  border-radius:999px;padding:3px 11px;font-size:12.5px;
  font-family:Consolas,"Courier New",monospace;cursor:default;
}
.chip.installed{background:#eefaf2;border-color:#c6ecd4;color:#166534}

/* ---------- 进度条 ---------- */
.progress{height:8px;border-radius:999px;background:#e6edf5;overflow:hidden}
.progress-fill{
  height:100%;border-radius:999px;
  background:linear-gradient(90deg,var(--blue),#4da3f5);
  transition:width .3s ease; width:0;
}
.progress-fill.done{background:linear-gradient(90deg,#16a34a,#4ade80)}
.progress-fill.failed{background:linear-gradient(90deg,#dc2626,#f87171)}
.progress-fill.canceled{background:#94a3b8}

/* ---------- 任务 ---------- */
.task-list{display:flex;flex-direction:column;gap:12px}
.task{
  border:1px solid var(--line);border-radius:12px;background:#fff;padding:13px 15px;
}
.task-head{display:flex;align-items:flex-start;gap:10px;flex-wrap:wrap}
.task-title{font-weight:700;font-size:14.5px;word-break:break-all}
.task-meta{display:flex;gap:7px;align-items:center;flex-wrap:wrap;margin-top:3px}
.task-body{margin-top:9px}
.task-phase{display:flex;justify-content:space-between;gap:10px;font-size:12.5px;color:var(--muted);margin-bottom:5px;flex-wrap:wrap}
.task-actions{margin-left:auto;display:flex;gap:7px;align-items:center;flex-wrap:wrap}
.task-error{
  margin-top:9px;background:var(--red-bg);border:1px solid #f6c9c9;color:#9c1c1c;
  border-radius:9px;padding:9px 12px;font-size:12.5px;white-space:pre-wrap;word-break:break-all;
}
.task-success{
  margin-top:9px;background:var(--green-bg);border:1px solid #c6ecd4;color:#166534;
  border-radius:9px;padding:8px 12px;font-size:12.5px;white-space:pre-wrap;word-break:break-all;
}
details.logs{margin-top:9px}
details.logs summary{
  cursor:pointer;color:var(--blue);font-size:12.5px;user-select:none;outline:none;
  list-style:none;
}
details.logs summary::-webkit-details-marker{display:none}
details.logs summary::before{content:"\25B8 ";display:inline-block;transition:transform .15s}
details.logs[open] summary::before{content:"\25BE "}
.log-pre{
  margin:8px 0 0;background:#0f172a;color:#d8e4f5;border-radius:9px;
  padding:10px 12px;font-family:Consolas,"Courier New",monospace;font-size:12px;
  line-height:1.65;max-height:260px;overflow:auto;white-space:pre-wrap;word-break:break-all;
}
.log-pre .l-time{color:#7fa6d4}

/* ---------- 整合包 ---------- */
.pack-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(300px,1fr));gap:12px}
.pack-card{
  border:1px solid var(--line);border-radius:12px;background:#fff;padding:12px;
  display:flex;gap:12px;align-items:flex-start;min-width:0;
}
.pack-card:hover{border-color:#cfdded;box-shadow:var(--shadow)}
.pack-icon{
  width:56px;height:56px;border-radius:11px;flex:0 0 auto;object-fit:cover;
  background:#eef2f7;border:1px solid var(--line);
}
.pack-icon.placeholder{
  display:flex;align-items:center;justify-content:center;font-size:22px;
  color:#94a3b8;background:#f1f5f9;
}
.pack-main{min-width:0;flex:1}
.pack-title{font-weight:700;font-size:14px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.pack-desc{
  color:var(--muted);font-size:12.5px;margin:3px 0 6px;
  display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden;
}
.pack-meta{display:flex;gap:8px;flex-wrap:wrap;align-items:center;font-size:11.5px;color:var(--muted)}
.pack-actions{margin-top:8px;display:flex;gap:7px}

/* ---------- 设置 ---------- */
.form-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px 18px}
.field{display:flex;flex-direction:column;gap:5px;min-width:0}
.field label{font-size:12.5px;color:#475569;font-weight:600}
.field .hint{font-size:11.5px;color:var(--muted)}
.field.full{grid-column:1 / -1}
.field .input,.field .select{width:100%}
.check{display:flex;align-items:center;gap:8px;font-size:13px;color:#334155;cursor:pointer}
.check input{width:16px;height:16px;accent-color:var(--blue);cursor:pointer}
.form-actions{display:flex;gap:9px;margin-top:16px;flex-wrap:wrap}

/* ---------- 关于 ---------- */
.kv{display:grid;grid-template-columns:120px 1fr;gap:7px 14px;font-size:13px}
.kv .k{color:var(--muted)}
.kv .v{font-family:Consolas,"Courier New",monospace;word-break:break-all}
.help-list{margin:0;padding-left:20px;color:#334155;font-size:13.5px}
.help-list li{margin:5px 0}
.code{
  background:#f6f8fb;border:1px solid var(--line);border-radius:7px;padding:1px 6px;
  font-family:Consolas,"Courier New",monospace;font-size:12.5px;color:#334155;
}

/* ---------- 空状态 / 加载 / 提示 ---------- */
.empty{
  padding:30px 18px;text-align:center;color:var(--muted);font-size:13px;
  border:1px dashed var(--border);border-radius:11px;background:#fbfcfe;
}
.empty .big{font-size:26px;display:block;margin-bottom:6px;opacity:.65}
.error-box{
  background:var(--red-bg);border:1px solid #f6c9c9;color:#9c1c1c;
  border-radius:10px;padding:12px 14px;font-size:13px;margin-bottom:12px;
  display:flex;gap:12px;align-items:flex-start;flex-wrap:wrap;
}
.error-box .msg{flex:1;word-break:break-all}
.hint{
  background:#eef5ff;border:1px solid #d3e3f8;color:#1e4f86;
  border-radius:10px;padding:10px 13px;font-size:13px;margin-bottom:12px;
}
.hint.warn{background:var(--orange-bg);border-color:#f3ddb9;color:#8a5a10}
.loading{display:flex;align-items:center;gap:9px;color:var(--muted);padding:22px 6px;font-size:13px}
.spinner{
  width:16px;height:16px;border-radius:50%;border:2px solid #d6e2f0;border-top-color:var(--blue);
  animation:spin .8s linear infinite;flex:0 0 auto;
}

/* ---------- 分页 ---------- */
.pager{display:flex;align-items:center;gap:10px;margin-top:12px;flex-wrap:wrap;justify-content:flex-end}
.pager .muted{font-size:12.5px}

/* ---------- Toast ---------- */
.toasts{
  position:fixed;right:16px;bottom:16px;z-index:99;
  display:flex;flex-direction:column;gap:9px;max-width:min(92vw,380px);
}
.toast{
  background:#fff;border:1px solid var(--border);border-left:4px solid var(--blue);
  border-radius:10px;padding:10px 13px;font-size:13px;box-shadow:0 6px 22px rgba(15,42,74,.16);
  animation:toastIn .18s ease-out;cursor:pointer;word-break:break-all;
}
.toast.success{border-left-color:var(--green)}
.toast.error{border-left-color:var(--red)}
.toast.warn{border-left-color:var(--orange)}
.toast .t-title{font-weight:700;margin-bottom:1px}
.toast .t-body{color:#475569;font-size:12.5px}
@keyframes toastIn{from{opacity:0;transform:translateY(8px)}to{opacity:1;transform:none}}

/* ---------- 响应式 ---------- */
@media (max-width:900px){
  .stats{grid-template-columns:repeat(2,1fr)}
  .form-grid{grid-template-columns:1fr}
  .topbar-row{padding:9px 12px 7px}
  .container{padding:12px 12px 48px}
  .brand-sub{display:none}
  .topbar-actions .hide-sm{display:none}
}
@media (max-width:620px){
  .brand-title{font-size:15px}
  .hero{padding:18px}
  .hero h1{font-size:19px}
  .stats{grid-template-columns:1fr 1fr;gap:9px}
  .card{padding:13px 12px;border-radius:11px}
  .pack-grid{grid-template-columns:1fr}
  .table th:nth-child(3),.table td:nth-child(3){display:none}
  .kv{grid-template-columns:1fr;gap:2px}
  .kv .k{margin-top:7px}
  .task-actions{margin-left:0;width:100%}
}
""";
    }
}