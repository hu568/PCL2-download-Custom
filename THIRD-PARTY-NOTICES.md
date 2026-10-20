# 第三方声明与许可（THIRD-PARTY NOTICES）

本项目：**PCL 下载中心（第三方修改版）** · 仓库：https://github.com/hu568/PCL2-download-Custom

## 1. 一句话结论

- 本项目是**第三方基于 PCL 独立进行二次创作的产物**，与 PCL 官方（龙腾猫跃 / 成都瓜皮龙科技有限公司）、Mojang、Microsoft 均无关联。
- 本项目**不分发任何 PCL 代码或二进制**：发布包内只有我们自己编译的 `PCLDownloadCenter.exe`，以及 MIT 许可的 `Newtonsoft.Json.dll`。
- 本项目自身以 **GNU GPL-3.0** 发布（见仓库根目录 `LICENSE`）。

## 2. 上游 PCL

| 项目 | 说明 |
| --- | --- |
| 上游软件 | PCL（Plain Craft Launcher 2），作者 **龙腾猫跃**（LTCat），版权 © 成都瓜皮龙科技有限公司，计算机软件著作权登记号 2020SR0875133 |
| 上游源码 | https://github.com/Meloong-Git/PCL · https://github.com/Hex-Dragon/PCL2 |
| 上游许可 | 《PCL 分发有限许可》（https://shimo.im/docs/rGrd8pY8xWkt6ryW#anchor-X0bo）+《PCL 存储库合理使用指南》——**均非 OSI 认可的开源许可**，除明确授予的权利外，开发者保留全部权利 |
| 上游指南全文 | 见本仓库 `LICENCE-PCL.md`（原样保留，按要求继续作为本项目的指南） |
| 赞助链接 | https://meloong.com/afd/a/LTCat |
| 我们是否分发 PCL 代码/二进制 | **否**。本插件运行在 PCL 的主页插件机制里；安装 Forge / NeoForge 时，会从**用户本机已安装**的 PCL 程序中提取其内嵌的补丁版安装器（PCL 自身也用它），文件始终留在用户本机 |

按《PCL 存储库合理使用指南》对「重度使用」的要求，本项目已落实：

1. 明确声明本文档第 1 节中的第三方二次创作身份；
2. 软件名以 PCL 开头并带第三方标识（「PCL 下载中心（**第三方修改版**）」）；
3. 在插件主页的「**关于**」页首位给出原作者龙腾猫跃的署名与赞助链接，公开源代码，并继续使用上游指南文件；
4. 本插件**没有** Minecraft 启动功能（启动与正版购买劝导由 PCL 本体负责，见 PCL 的 `McLaunchPrecheck`），因此该项不适用；
5. 没有任何类似 PCL「赞助解锁」的功能或表现：界面沿用 PCL 内置控件与其默认蓝色主色调。

## 3. 随发布包分发的第三方组件

### Newtonsoft.Json 13.0.3

- 用途：JSON / JSON5 解析与序列化（`PCLDownloadCenter.exe` 的运行时依赖）
- 许可：MIT
- 文件：`PCL\DownloadCenter\Newtonsoft.Json.dll`

```text
The MIT License (MIT)

Copyright (c) 2007 James Newton-King

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## 4. 仅在构建期使用、不随包分发的工具

| 组件 | 许可 | 说明 |
| --- | --- | --- |
| Roslyn 编译器 `csc.exe` | MIT（Microsoft） | `tools/build.ps1` 编译用；位于被 gitignore 的 `tools/nuget/`，不随发布包分发 |
| .NET Framework 参考程序集（`refasm`） | Microsoft .NET Framework 参考程序集许可 | 仅编译期引用；不随发布包分发 |
| `ref/PCL` 参考源码 | 上述上游许可（《PCL 分发有限许可》+《PCL 存储库合理使用指南》） | 仅作开发期阅读参考，被 gitignore，**不随发布包分发，也不参与编译** |
| `ref/PCL2-CE` 参考源码（另一社区分支） | 仓库根目录为 Apache-2.0，但其 `Plain Craft Launcher 2/` 目录另有 `LICENCE`（同为《PCL 分发有限许可》全文），**该目录不适用 Apache-2.0** | 同上，不分发、不参与编译 |

## 5. Minecraft 相关声明

- 本项目不包含、也不分发任何 Minecraft 游戏本体或受版权保护的游戏文件；游戏文件均由使用者通过 Mojang 官方 / 社区公开接口在本机下载。
- 本项目非 MINECRAFT 官方产品。未经 MOJANG 或 MICROSOFT 批准，也不与 MOJANG 或 MICROSOFT 关联。
- 使用者应自行遵守所在地法律法规与 Minecraft 最终用户许可协议（EULA）；下载即视为同意 EULA。

## 6. 发布包内应包含的许可文件

```text
LICENSE                 本项目许可（GNU GPL-3.0）
LICENCE-PCL.md          上游 PCL 许可与合理使用指南（原文）
THIRD-PARTY-NOTICES.md  本文件：第三方声明与组件许可全文
PCL\DownloadCenter\     上述文件的副本随可执行文件一起安装
```

如果你**没有**在发布包里看到这些文件，说明它是被二次打包、可能被改过的版本，请从官方仓库获取：
https://github.com/hu568/PCL2-download-Custom
