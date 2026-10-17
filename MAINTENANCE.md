# 分支维护说明（own/v2）

本分支是我们自维护的整合分支，产品名 **MeowDown**（2026-10-17 由 BBDownT 更名而来，见文末"更名记录"）。策略：**以 LOVAHE/BBDownT 的 v2 分支为基线，吸收 MOPELotus/BBDown 的修补，保留对 nilaoda/BBDown 上游的追踪能力**。

## 三个来源

| 来源 | 分支 | 状态 | 定位 |
| --- | --- | --- | --- |
| `origin`（nilaoda/BBDown） | `master`（本地 `upstream-master`） | **已归档**（2026-05-14），默认分支被替换为只含归档声明的 `DELETED` 分支 | 协议实现的源头，不再有更新；保留 remote 以便查阅 |
| `fork-lovahe`（LOVAHE/BBDownT） | `v2` | **活跃**（2.2.0-beta），本分支的直接基线 | 深度重构：+3 万行，含测试项目、CodeQL、cookie 自动刷新、服务器鉴权、自更新、迁移工具 |
| `fork-mopelotus`（MOPELotus/BBDown） | `master` | 低频维护（1.6.4） | 对上游经典代码库的精准修补（+741 行）：Set-Cookie 登录解析、WBI 模块化、凭据续期 |

## 为什么以 BBDownT v2 为基线

> 本节为基线时点（2026-10-16）与 MOPELotus 的对照记录；表中文件路径按当前 MeowDown 命名显示。

经逐项核对，MOPELotus 的实质贡献在 BBDownT v2 中均有等价或更好的实现：

| MOPELotus 的改动 | BBDownT v2 对应物（现路径） |
| --- | --- |
| 登录 cookie 从 Set-Cookie 头提取（白名单解析） | `MeowDownLoginUtil.cs` 同样解析 Set-Cookie，另含国际站登录（`IntlLoginClient`） |
| `BilibiliCredentialRefresher` 凭据自动续期 | `MeowDownCookieRefreshUtil`（含单元测试 `CookieRefreshTests`） |
| WBI 签名模块化（`BilibiliWebUtil`） | `BrowserRequestProfile` / `DeviceCookieHandler` 更完整的浏览器指纹模拟 |
| 版本比较修复 | `ReleaseVersion.TryParse` |
| 空间投稿批量导出 | `SpaceBatchDownload` 直接支持批量下载 |
| 测试项目 | `MeowDown.Tests`（1600+ 用例）+ CodeQL |

BBDownT v2 还额外修复了上游审计中确认的多处问题（混流退出码丢弃、DOVI 版本判断列索引错误、多 P 归档误跳过、弹幕颜色 RGB/BGR 颠倒、TLS 证书校验全局关闭改为 `ALLOW_INSECURE_TLS` 显式开启、API 服务器无鉴权等），详见各 commit。

## 与基线的同步策略

1. 定期 `git fetch fork-lovahe`，将其 `v2` 的新提交合并进本分支（本分支 tracking `fork-lovahe/v2`，直接 `git merge` 即可）。
2. MOPELotus 若有 BBDownT 未覆盖的新修补，逐个 cherry-pick 并验证。
3. 上游已归档，仅在需要对照协议实现时查阅 `upstream-master`。
4. 本分支自己的改动独立成 commit，不与上游提交混写，便于 rebase。

## 本分支自有改动

- `MeowDown/MeowDownApiServer.cs`：服务器任务 WorkDir 校验在环境变量展开**之前**拒绝内嵌 NUL 的路径。Windows 的 `ExpandEnvironmentVariables` 经 Win32 会在 NUL 处截断字符串，导致 `Path.GetFullPath` 收到截断后的合法路径，带 NUL 的目录名绕过校验被静默替换为截断名（Linux 上为托管实现、`GetFullPath` 抛异常，故上游测试未暴露此平台差异）。
- `MeowDown.Tests/IntlMediaRequestTests.cs`：aria2c 参数断言改为用 `Path.GetDirectoryName` 生成期望目录，不再硬编码 POSIX 分隔符，Windows 上可正确运行。

本机验证：`dotnet build` 0 警告 0 错误；`dotnet test` 1611 通过 / 0 失败 / 9 跳过（跳过项为需要真实网络的协议联调测试）。

## 更名记录（2026-10-17）

产品名由 BBDownT 更名为 **MeowDown**：目录（`MeowDown`/`MeowDown.Core`/`MeowDown.Tests`）、命名空间、类名前缀、程序集与 NuGet 包名（`PackageId=MeowDown`）、命令名（`ToolCommandName=MeowDown`）、磁盘文件名（`MeowDown.data`/`MeowDown.config`/`MeowDown.archives`/`MeowDownIntl.data` 等）全部更替。保留两类不改：上游 LOVAHE/BBDownT 仓库 URL（含自更新 feed）；旧版 BBDown 的 `BBDownTV.data` 等遗留文件名。`--migrate` 的迁移表新增 BBDownT 时代文件名 → MeowDown 文件名的映射，BBDownT 老用户凭据可无缝迁移；`SelfUpdater` 的资产名模式随更名变为 `MeowDown_*`，在上游 feed 中将匹配不到资产，表现为"已是最新版本"，属预期降级。CI 的 `publish-nuget.yml` 会以 `MeowDown` 包名推送，在 NuGet 上取得该 id 所有权之前发布会失败。

## 构建注意（Windows，仅装了 .NET 10 运行时时）

项目目标框架为 net9.0。SDK 10 可正常构建；运行/测试需 `DOTNET_ROLL_FORWARD=LatestMajor` 环境变量，或安装 .NET 9 运行时。
