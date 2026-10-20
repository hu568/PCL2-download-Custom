// Web UI 页面结构（内嵌到程序集）
namespace PCLDownloadCenter
{
    internal static class UiHtml
    {
        public const string Body = """
<header class="topbar">
  <div class="topbar-row">
    <div class="brand">
      <div class="logo">PCL</div>
      <div class="brand-text">
        <div class="brand-title">{{APPNAME}}</div>
        <div class="brand-sub" id="brandSub">正在连接本地服务…</div>
      </div>
    </div>
    <div class="topbar-actions">
      <div class="status-pill" id="statusPill" title="本地服务状态">
        <span class="dot" id="statusDot"></span><span id="statusText">连接中</span>
      </div>
      <div class="metric-pill hide-sm" id="taskPill" title="任务数量">
        <span class="metric-num" id="runningCount">0</span>进行中<span class="metric-sep">/</span><span class="metric-num" id="queuedCount">0</span>排队
      </div>
      <button class="btn ghost sm" data-act="publish" title="重写 Custom.xaml，然后在 PCL 主界面点『刷新主页』">刷新 PCL 主页</button>
      <button class="btn ghost sm hide-sm" data-act="goto" data-view="settings">设置</button>
    </div>
  </div>
  <nav class="tabs" id="navTabs">
    <button class="tab active" data-act="goto" data-view="home">首页</button>
    <button class="tab" data-act="goto" data-view="vanilla">原版游戏</button>
    <button class="tab" data-act="goto" data-view="loader">加载器</button>
    <button class="tab" data-act="goto" data-view="pack">整合包</button>
    <button class="tab" data-act="goto" data-view="tasks">任务<span class="tab-badge" id="tabTaskBadge" hidden>0</span></button>
    <button class="tab" data-act="goto" data-view="settings">设置</button>
    <button class="tab" data-act="goto" data-view="about">帮助</button>
  </nav>
</header>

<main class="container">
  <!-- ============ 首页 ============ -->
  <section class="view active" id="view-home">
    <div class="hero">
      <h1>{{APPNAME}}</h1>
      <p>为 PCL2 2.99 补回原版游戏 / 加载器 / 整合包安装能力。任务在后台进行，进度会实时显示在这里。</p>
      <div class="hero-actions">
        <button class="btn" id="homeInstallRelease" data-act="home-install-release">安装最新正式版</button>
        <button class="btn ghost" id="homeInstallSnapshot" data-act="home-install-snapshot">安装最新快照</button>
        <button class="btn ghost" data-act="goto" data-view="vanilla">浏览全部版本</button>
        <button class="btn ghost" data-act="publish">刷新 PCL 主页</button>
      </div>
    </div>

    <div class="stats">
      <div class="card stat"><div class="stat-num" id="statInstalled">-</div><div class="stat-label">已安装版本</div></div>
      <div class="card stat"><div class="stat-num" id="statRunning">0</div><div class="stat-label">进行中 / 排队任务</div></div>
      <div class="card stat"><div class="stat-num" id="statSource">-</div><div class="stat-label">当前下载源</div></div>
      <div class="card stat clickable" id="statMcCard" title="点击复制"><div class="stat-num" id="statMc" style="font-size:13px;line-height:1.5">-</div><div class="stat-label">Minecraft 目录（点击复制）</div></div>
    </div>

    <div class="card">
      <div class="card-head"><h2>最近任务</h2><span class="spacer"></span><button class="btn ghost sm" style="color:var(--text)" data-act="goto" data-view="tasks">全部任务</button></div>
      <div id="homeTasks"><div class="empty"><span class="big">🗒️</span>还没有任务，去「原版游戏」页安装一个版本试试。</div></div>
    </div>

    <div class="card">
      <div class="card-head"><h2>已安装版本</h2><span class="muted" id="installedSummary"></span><span class="spacer"></span><button class="btn ghost sm" style="color:var(--text)" data-act="reload-installed">刷新</button></div>
      <div id="installedList"><div class="empty">加载中…</div></div>
    </div>
  </section>

  <!-- ============ 原版游戏 ============ -->
  <section class="view" id="view-vanilla">
    <div class="card">
      <div class="card-head">
        <h2>原版游戏</h2>
        <span class="muted" id="vanillaSummary"></span>
        <span class="spacer"></span>
        <button class="btn ghost sm" style="color:var(--text)" data-act="vanilla-refresh">刷新清单</button>
      </div>
      <div class="toolbar">
        <div class="seg" id="vanillaFilter">
          <button class="seg-btn active" data-act="vanilla-filter" data-filter="release">正式版</button>
          <button class="seg-btn" data-act="vanilla-filter" data-filter="snapshot">快照</button>
          <button class="seg-btn" data-act="vanilla-filter" data-filter="old">远古版本</button>
          <button class="seg-btn" data-act="vanilla-filter" data-filter="all">全部</button>
        </div>
        <input class="input grow" id="vanillaSearch" placeholder="搜索版本号，例如 1.21 / 1.7.10" />
        <select class="select sm" id="vanillaPageSize" title="每页数量">
          <option value="25">25 条/页</option>
          <option value="50" selected>50 条/页</option>
          <option value="100">100 条/页</option>
        </select>
      </div>
      <div id="vanillaBody"><div class="loading"><span class="spinner"></span>正在获取版本清单…</div></div>
      <div class="pager">
        <button class="btn ghost sm" style="color:var(--text)" data-act="vanilla-prev" id="vanillaPrev">上一页</button>
        <span class="muted" id="vanillaPageInfo">-</span>
        <button class="btn ghost sm" style="color:var(--text)" data-act="vanilla-next" id="vanillaNext">下一页</button>
      </div>
    </div>
  </section>

  <!-- ============ 加载器 ============ -->
  <section class="view" id="view-loader">
    <div class="card">
      <div class="card-head"><h2>安装加载器</h2><span class="muted">支持 Fabric / Quilt / Forge / NeoForge（OptiFine、LiteLoader 暂不支持）；先选类型与 MC 版本</span></div>
      <div class="toolbar">
        <select class="select" id="loaderKind" title="加载器类型">
          <option value="fabric">Fabric</option>
          <option value="quilt">Quilt</option>
          <option value="forge">Forge</option>
          <option value="neoforge">NeoForge</option>
        </select>
        <select class="select grow" id="loaderMc" title="Minecraft 版本"><option value="">正在加载 MC 版本…</option></select>
        <button class="btn primary" data-act="loader-load" id="loaderLoadBtn">获取版本列表</button>
      </div>
      <div id="loaderHint"></div>
      <div id="loaderList"><div class="empty"><span class="big">🧩</span>选择加载器类型与 MC 版本后，点击「获取版本列表」。</div></div>
    </div>
  </section>

  <!-- ============ 整合包 ============ -->
  <section class="view" id="view-pack">
    <div class="card">
      <div class="card-head"><h2>整合包</h2><span class="muted">搜索数据来自 Modrinth，安装后会在 PCL 版本列表中出现</span></div>
      <div class="toolbar">
        <input class="input grow" id="packQuery" placeholder="搜索整合包，例如 Create / 生存 / 冒险" />
        <select class="select" id="packGameVersion" title="游戏版本"><option value="">全部游戏版本</option></select>
        <select class="select" id="packLoader" title="加载器">
          <option value="">全部加载器</option>
          <option value="fabric">Fabric</option>
          <option value="forge">Forge</option>
          <option value="quilt">Quilt</option>
          <option value="neoforge">NeoForge</option>
        </select>
        <button class="btn primary" data-act="pack-search">搜索</button>
      </div>
      <div class="toolbar">
        <input class="input grow" id="packIdInput" placeholder="或直接输入 Modrinth 整合包 ID / 链接，例如 https://modrinth.com/modpack/xxx" />
        <input class="input" id="packVersionInput" style="flex:0 0 200px" placeholder="版本 ID（可选，如 37TmHvhB）" />
        <button class="btn" data-act="pack-install-id">直接安装</button>
      </div>
      <div class="toolbar">
        <input class="input grow" id="packCfInput" placeholder="或输入 CurseForge 整合包 ID（需要 CurseForge API Key）" />
        <button class="btn" data-act="pack-install-cf">从 CurseForge 安装</button>
      </div>
      <div class="hint warn" id="packCfHint">CurseForge 在线安装需要 API Key（可在「设置」中填写）；当前 PCL 使用的第三方代理不可用，没有 Key 时官方接口返回 403。建议优先使用上面的 Modrinth 搜索安装。</div>
      <div id="packResults"></div>
      <div class="pager">
        <button class="btn ghost sm" style="color:var(--text)" data-act="pack-prev">上一页</button>
        <span class="muted" id="packPageInfo">-</span>
        <button class="btn ghost sm" style="color:var(--text)" data-act="pack-next">下一页</button>
      </div>
    </div>
  </section>

  <!-- ============ 任务 ============ -->
  <section class="view" id="view-tasks">
    <div class="card">
      <div class="card-head">
        <h2>任务</h2>
        <span class="muted" id="taskSummary"></span>
        <span class="spacer"></span>
        <button class="btn ghost sm" style="color:var(--text)" data-act="tasks-refresh">刷新</button>
      </div>
      <div id="taskList"><div class="empty"><span class="big">📋</span>暂无任务</div></div>
    </div>
  </section>

  <!-- ============ 设置 ============ -->
  <section class="view" id="view-settings">
    <div class="card">
      <div class="card-head"><h2>设置</h2><span class="muted">保存后写入 config.json；端口修改需要重启服务</span></div>
      <div id="settingsHint"></div>
      <div class="form-grid">
        <div class="field">
          <label for="setDownloadSource">下载源</label>
          <select class="select" id="setDownloadSource">
            <option value="official">官方源（Mojang / Forge / Fabric 官方）</option>
            <option value="bmclapi">BMCLAPI 镜像（国内加速）</option>
          </select>
          <span class="hint">官方源可直连；下载慢或失败时切换镜像。</span>
        </div>
        <div class="field">
          <label for="setThreads">下载线程数</label>
          <input class="input" id="setThreads" type="number" min="1" max="128" step="1" />
          <span class="hint">1 ~ 128，默认 32。</span>
        </div>
        <div class="field">
          <label for="setServerPort">服务端口</label>
          <input class="input" id="setServerPort" type="number" min="1024" max="65535" step="1" />
          <span class="hint">修改后需要重启后台服务才会生效。</span>
        </div>
        <div class="field">
          <label for="setMaxConcurrentTasks">同时运行的安装任务数</label>
          <input class="input" id="setMaxConcurrentTasks" type="number" min="1" max="4" step="1" />
          <span class="hint">1 ~ 4，默认 1。</span>
        </div>
        <div class="field full">
          <label for="setMcDir">Minecraft 目录（.minecraft）</label>
          <input class="input mono" id="setMcDir" placeholder="例如 D:\PCL2\.minecraft" />
          <span class="hint">保存后立即生效，之后的安装会写入该目录。</span>
        </div>
        <div class="field full">
          <label for="setJavaPath">Java 路径（可选）</label>
          <input class="input mono" id="setJavaPath" placeholder="留空自动探测，例如 C:\Program Files\Java\jdk-17\bin\java.exe" />
        </div>
        <div class="field full">
          <label for="setCfKey">CurseForge API Key（可选）</label>
          <input class="input mono" id="setCfKey" placeholder="需要 CurseForge API Key；没有 Key 时请使用 Modrinth" />
        </div>
        <div class="field">
          <label for="setHomepageMaxVersions">主页显示版本数量</label>
          <input class="input" id="setHomepageMaxVersions" type="number" min="5" max="200" step="1" />
        </div>
        <div class="field">
          <label>其它选项</label>
          <label class="check"><input type="checkbox" id="setHomepagePublish" /> 任务变化后自动重写 PCL 主页</label>
          <label class="check"><input type="checkbox" id="setHomepageShowSnapshots" /> 主页显示快照版本</label>
          <label class="check"><input type="checkbox" id="setUseMirrorFallback" /> 下载失败时自动切换镜像（回退）</label>
        </div>
      </div>
      <div class="form-actions">
        <button class="btn primary" data-act="save-config" id="saveConfigBtn">保存设置</button>
        <button class="btn" data-act="reload-config">重新读取</button>
        <button class="btn" data-act="publish">重写 PCL 主页</button>
      </div>
    </div>
  </section>

  <!-- ============ 帮助 ============ -->
  <section class="view" id="view-about">
    <div class="card">
      <div class="card-head"><h2>关于 / 致谢</h2><span class="muted">PCL 下载中心（第三方修改版）</span></div>
      <p class="muted" style="margin:0 0 8px">PCL（Plain Craft Launcher 2）原作者：<strong>龙腾猫跃（LTCat）</strong>。本插件是第三方基于 PCL 独立进行二次创作的产物，与 PCL 官方无关。</p>
      <p class="muted" style="margin:0 0 4px">如果觉得 PCL 好用，欢迎赞助作者：<a href="https://meloong.com/afd/a/LTCat" target="_blank" rel="noreferrer">https://meloong.com/afd/a/LTCat</a></p>
      <div class="form-actions" style="margin-top:10px">
        <a class="btn primary" href="https://github.com/hu568/PCL2-download-Custom" target="_blank" rel="noreferrer">本项目 GitHub 仓库</a>
        <a class="btn" href="https://github.com/Meloong-Git/PCL" target="_blank" rel="noreferrer">PCL GitHub（维护版）</a>
        <a class="btn" href="https://github.com/Hex-Dragon/PCL2" target="_blank" rel="noreferrer">PCL 原版仓库</a>
        <a class="btn" href="https://shimo.im/docs/rGrd8pY8xWkt6ryW#anchor-X0bo" target="_blank" rel="noreferrer">PCL 分发有限许可</a>
      </div>
      <p class="muted" style="margin:10px 0 0">本项目以 GNU GPL-3.0 发布，源码即上面的 GitHub 仓库；遵循《PCL 存储库合理使用指南》，完整声明见发布包内的 LICENSE / LICENCE-PCL.md / THIRD-PARTY-NOTICES.md。</p>
    </div>
    <div class="card">
      <div class="card-head"><h2>帮助 / 关于</h2><span class="muted" id="aboutSummary"></span></div>
      <div id="aboutInfo"><div class="loading"><span class="spinner"></span>正在读取运行信息…</div></div>
    </div>
    <div class="card">
      <div class="card-head"><h2>怎么用</h2></div>
      <ol class="help-list">
        <li>在「原版游戏」页筛选并安装游戏版本；任务会自动在后台下载，进度在「任务」页实时显示。</li>
        <li>想装 Mod，先在「加载器」页安装 Fabric / Quilt / Forge / NeoForge，再让整合包安装自动补依赖。</li>
        <li>整合包可以从「整合包」页搜索安装，也可以把 .mrpack / .zip 拖给 PCL 主页插件的「导入整合包」。</li>
        <li>装完后切回 PCL 主界面，点击 <span class="code">刷新主页</span>（或重启 PCL）即可在版本列表里看到新实例。</li>
        <li>下载慢时到「设置」里把下载源切换为 BMCLAPI 镜像，或调高线程数。</li>
      </ol>
    </div>
    <div class="card">
      <div class="card-head"><h2>运行日志</h2><span class="muted" id="logFileText"></span><span class="spacer"></span><button class="btn ghost sm" style="color:var(--text)" data-act="reload-logs">刷新日志</button></div>
      <pre class="log-pre" id="logPre">点击「刷新日志」查看最近的运行日志。</pre>
    </div>
  </section>
</main>

<div class="toasts" id="toasts"></div>
""";
    }
}