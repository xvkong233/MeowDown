# 多用户 Web 服务 API 契约

本文档定义 MeowDown 服务器模式下的「多用户 Web 服务」契约：注册/登录、会话、B站账号绑定（扫码）、在线解析与下载任务、管理员配置与用户管理，以及同源托管的 Web 前端静态文件。

面向读者：MeowDown 服务端实现者与 Web 前端开发者。文档中的字段名、错误码与状态值即为契约，实现与前端均应按此对齐。

## 与现有实现的关系

- 现有 Token 鉴权 API（`/get-tasks*`、`/add-task`、`/remove-finished*`，见 [json-api-doc.md](../json-api-doc.md)）**保持不变**，继续供脚本/第三方集成使用；Token 与 Web 会话互不通用。
- 服务端骨架复用 `MeowDown/MeowDownApiServer.cs` 的 minimal API 与队列（`DownloadTaskStore` 内存队列，串行执行）；现有全局鉴权中间件（`MeowDownApiServer.cs:61-77`，Token 校验 + 本机直连校验）需按路由拆分：`/api/*` 与静态文件走 Web 会话，旧 API 走 Token。
- 扫码登录复用 `MeowDown/MeowDownLoginUtil.cs`：二维码地址 `passport.bilibili.com/x/passport-login/web/qrcode/generate`（`MeowDownLoginUtil.cs:199`），轮询 `qrcode/poll`，状态码 `86101`=等待、`86090`=已扫、`86038`=过期、`0`=成功（`MeowDownLoginUtil.cs:268-292`）；二维码 PNG 由 QRCoder 的 `PngByteQRCode.GetGraphic(...)` 服务端出图（同 `MeowDownLoginUtil.cs:142-143` 的现有用法）。
- 凭据落盘复用 `MeowDownCookieRefreshUtil.NormalizeLoginCookie`（`MeowDownCookieRefreshUtil.cs:118`）与 `HasRequiredLoginCookies` 校验（SESSDATA/bili_jct 缺失则不落盘，`MeowDownLoginUtil.cs:219-220`）；文件原子写入与权限收紧复用 `MeowDownLoginUtil.WritePrivateFile`（`MeowDownLoginUtil.cs:155`）。
- 解析复用 `Program.GetVideoInfoAsync`（`Program.cs:497`）；分页与清晰度字段来自 `VInfo.PagesInfo`（`MeowDown.Core/Entity/VInfo.cs:43`，`Page` 字段见 `MeowDown.Core/Entity/Entity.cs:8-33`）与清晰度表 `Config.qualitys`（`MeowDown.Core/Config.cs:35-41`）。
- 任务请求体复用 `ServeRequestOptions`（`MeowDown/Model/ServeRequestOptions.cs` 继承 `MeowDown/MyOption.cs`），校验复用 `ServerRequestValidator`（`MeowDown/ServerRequestValidator.cs`）；用户绑定的B站凭据经 `MyOption.Cookie` 注入（`SetUpWork` 中 `Config.COOKIE = myOption.Cookie`，`Program.cs:456`）。

## 启动引导与部署

`serve` 命令在现有参数（`Program.cs:260-319`）之外新增：

| 参数 | 说明 |
| --- | --- |
| `--web-data-dir <path>` | Web 服务数据目录（用户账号、绑定凭据、运行时配置），默认 `<程序目录>/web-data/` |
| `--web-admin-password <pwd>` | 引导管理员账号的初始密码；未提供时生成随机密码并打印到日志 |
| `--web-secure-cookie` | 强制会话 Cookie 附加 `Secure`（部署在 HTTPS 反代之后时使用） |
| `--web-allow-registration` | 允许自助注册的初始开关（可在运行时被管理员关闭） |

以上参数同样可写入 `MeowDown.config`（配置文件机制见 `MeowDown/MeowDownConfigParser.cs`）。

**管理员账号引导（首次启动）**：Web 用户库为空时，服务端自动创建管理员账号：

- 用户名固定为 `admin`，角色 `admin`。
- 初始密码：优先取 `--web-admin-password`；未配置时生成 16 位随机密码（大小写字母+数字），并**仅在服务器日志打印一次**：
  `已创建Web管理员账号: admin 初始密码: <password> （仅打印一次，请立即登录并修改密码）`
- 初始密码为「未修改」状态；管理员未修改密码前，每次登录响应都带 `passwordChangeRequired: true` 提示（见 `GET /api/auth/me`）。

**监听与 HTTPS**：沿用现有约束——服务本身只提供 HTTP（`MeowDownApiServer.cs:120-132`），HTTPS 由反向代理终止；经反代部署时必须配置 `--web-secure-cookie` 或在反代处还原 `X-Forwarded-Proto`（服务端据此给 Cookie 加 `Secure`）。

## 认证与会话

- **口令哈希**：PBKDF2-HMAC-SHA256，600,000 次迭代（.NET `Rfc2898DeriveBytes.Pbkdf2`），16 字节随机盐，32 字节派生密钥，校验用 `CryptographicOperations.FixedTimeEquals`。存储格式：`pbkdf2-sha256$<iterations>$<base64(salt)>$<base64(hash)>`。迭代数只增不减；校验时按存储值执行。
- **口令策略**：长度 8–128；用户名 `[A-Za-z0-9_-]{3,32}`，唯一（大小写不敏感比较、原样存储）。
- **会话**：登录成功后服务端生成 256 位随机会话 ID（仅存内存，重启即失效，全部用户需重新登录），通过 Cookie 下发：
  - Cookie 名：`MeowDownWebSession`；`HttpOnly`；`SameSite=Strict`；`Path=/`；有效期 7 天滑动续期（每次认证请求刷新），绝对上限 30 天。
  - `Secure`：请求经 HTTPS 到达（含可信反代的 `X-Forwarded-Proto: https`）或配置 `--web-secure-cookie` 时附加。
- **CSRF**：`SameSite=Strict` 为主要防线；另外所有变更类请求（POST/PATCH/DELETE）必须携带 `Content-Type: application/json`（否则 415），`/api/*` 不参与现有 CORS 放行。Web 前端必须同源部署（由本服务托管）。
- **角色**：`user` 与 `admin`。`admin` 拥有 `user` 的全部能力。
- **暴力破解防护**：登录失败按「IP+用户名」计数，15 分钟内失败满 5 次锁定该组合 15 分钟（返回 429）；注册每 IP 每小时最多 5 次。

### 统一错误格式

除登录/会话外的错误一律返回 JSON：

```json
{ "error": { "code": "invalid_credentials", "message": "用户名或密码错误" } }
```

| HTTP | 典型 `code` | 场景 |
| --- | --- | --- |
| 400 | `bad_request` / `validation_failed` | 字段缺失、格式非法、URL 非B站链接等 |
| 401 | `unauthenticated` | 未登录 / 会话过期 |
| 403 | `forbidden` | 已登录但角色不足、访问他人资源 |
| 404 | `not_found` | 用户/任务/绑定会话不存在 |
| 409 | `conflict` | 用户名已存在、已绑定B站账号、不能删除最后一个管理员等 |
| 415 | `unsupported_media_type` | 变更请求缺少 `Content-Type: application/json` |
| 429 | `rate_limited` / `queue_full` | 触发限流锁定 / 任务队列已满 |
| 502 | `upstream_error` | B站接口失败（解析、扫码轮询等） |
| 504 | `upstream_timeout` | B站接口超时（解析默认 30s 超时） |

### 鉴权方式速查

| 路由前缀 | 鉴权 |
| --- | --- |
| `/api/auth/login`、`/api/auth/register` | 无需会话 |
| `/api/*` 其余 | Web 会话 Cookie |
| `/api/admin/*` | Web 会话 + `admin` 角色 |
| `/get-tasks*`、`/add-task`、`/remove-finished*` | 旧 Token 鉴权（不变） |
| `/`、静态文件 | 无鉴权（同源页面自身调用 `/api/*` 完成认证） |

---

## 端点：注册 / 登录 / 会话

### `POST /api/auth/register` — 自助注册

`allowRegistration` 关闭时返回 `403`（`code: registration_disabled`）。

请求：

```json
{ "username": "alice", "password": "s3cret-pw" }
```

成功：`201 Created`

```json
{ "id": "u_9f8a...", "username": "alice", "role": "user" }
```

错误：`400 validation_failed`（用户名/密码不合规则）；`403 registration_disabled`；`409 conflict`（用户名已存在）；`429 rate_limited`。

### `POST /api/auth/login` — 登录

请求：

```json
{ "username": "alice", "password": "s3cret-pw" }
```

成功：`200 OK`，响应头 `Set-Cookie: MeowDownWebSession=<id>; HttpOnly; SameSite=Strict; Path=/; Max-Age=604800`

```json
{
  "id": "u_9f8a...",
  "username": "alice",
  "role": "user",
  "passwordChangeRequired": false,
  "bilibiliBound": true
}
```

错误：`401 invalid_credentials`；`429 rate_limited`（连续失败锁定）。

### `POST /api/auth/logout` — 登出

撤销当前服务端会话并清除 Cookie。成功：`204 No Content`。幂等。

### `GET /api/auth/me` — 当前用户

成功：`200 OK`，响应体同登录成功响应；未登录返回 `401 unauthenticated`。

`passwordChangeRequired` 为 `true` 时（admin 初始密码未修改、或被管理员重置后首次登录），前端应引导修改密码。

### `POST /api/auth/password` — 修改自己的密码

请求：

```json
{ "currentPassword": "s3cret-pw", "newPassword": "n3w-secret-pw" }
```

成功：`204 No Content`；**撤销除当前会话外的全部会话**。错误：`400 validation_failed`；`401 invalid_credentials`（当前密码错误）。

---

## 端点：B站账号绑定（扫码）

- 绑定会话保存在内存中，按用户隔离：同一用户再次 `start` 会使上一个绑定会话失效。
- 二维码有效期跟随B站（约 180 秒）；过期后需重新 `start`。
- 服务端轮询B站的节流间隔为 1 秒（沿用 `MeowDownLoginUtil.WaitForQrLoginAsync` 的节奏，`MeowDownLoginUtil.cs:20-41`）；多个前端并发轮询同一 bindId 共享同一次上游结果。
- 绑定成功后凭据按用户隔离保存到 `web-data/bili/<userId>.json`（内容：cookie 头、refresh_token、mid、uname、绑定时间；`WritePrivateFile` 原子写入）。
- 绑定后，该用户的解析与下载任务自动使用其B站凭据（注入 `MyOption.Cookie`，见 `Program.cs:456`）；**未绑定的用户任务不携带任何B站 Cookie，也不使用服务器全局 Cookie**。

### `POST /api/bilibili/qr/start` — 发起扫码绑定

请求体为空（`{}`）。成功：`200 OK`

```json
{
  "bindId": "b_1c2d...",
  "qrUrl": "https://passport.bilibili.com/h5-app/passport/login/scan?navhide=1&qrcode_key=...",
  "expiresAt": 1769347200
}
```

- `qrUrl`：二维码内容（B站返回的登录 URL）。前端可用 `image` 端点取图，也可自行用 `qrUrl` 渲染。

### `GET /api/bilibili/qr/{bindId}/image` — 二维码图片

返回 `image/png`（`PngByteQRCode` 服务端渲染，白底黑码，缩放 8px/模块，带 `Cache-Control: no-store`）。bindId 不存在或已结束返回 `404 not_found`。无需额外参数。

### `GET /api/bilibili/qr/{bindId}` — 轮询绑定状态

前端每 1–2 秒调用一次，直到终态。

成功：`200 OK`

```json
{ "bindId": "b_1c2d...", "status": "scanned" }
```

`status` 取值：

| 值 | 含义 | 终态 |
| --- | --- | --- |
| `waiting` | 未扫码 | 否 |
| `scanned` | 已扫码待确认 | 否 |
| `success` | 绑定成功（凭据已按用户落盘） | 是 |
| `expired` | 二维码过期，需重新 start | 是 |
| `failed` | 上游错误或凭据落盘失败（如缺少 SESSDATA/bili_jct） | 是 |

`status=success` 时附账号摘要：

```json
{ "status": "success", "mid": "12345", "uname": "某用户" }
```

`status=failed` 时附 `{ "message": "..." }`。

错误：`404 not_found`。

### `GET /api/bilibili/bindings` — 查看我的绑定

成功：`200 OK`

```json
{ "bound": true, "mid": "12345", "uname": "某用户", "boundAt": 1769347000 }
```

未绑定时 `{ "bound": false }`。

### `DELETE /api/bilibili/bindings` — 解绑

删除该用户的B站凭据文件。成功：`204 No Content`。之后的解析/下载恢复为无 Cookie（受清晰度等限制）。

---

## 端点：在线解析与下载

### `POST /api/parse` — 解析元数据（不下载）

提交 URL，返回标题/分P/清晰度等元数据；**不创建任务、不产生下载**。服务端调用 `Program.GetVideoInfoAsync`，使用当前用户绑定的B站凭据（未绑定则无 Cookie，可用清晰度受限）。默认 30 秒超时。

请求：

```json
{ "url": "BV1qt4y1X7TW" }
```

URL 规则与任务一致：B站链接或 `av|bv|BV|ep|ss` 编号（`ServerRequestValidator.cs:27-30`）。

成功：`200 OK`

```json
{
  "aid": "170001",
  "bvid": "BV1qt4y1X7TW",
  "title": "视频标题",
  "desc": "视频简介",
  "pic": "https://i0.hdslb.com/bfs/archive/....jpg",
  "pubTime": 1600000000,
  "isBangumi": false,
  "pages": [
    { "index": 1, "cid": "278734", "title": "P1 名称", "durationSeconds": 300, "cover": "https://..." }
  ],
  "qualities": [
    { "id": 120, "name": "4K 超清" },
    { "id": 116, "name": "1080P 高帧率" },
    { "id": 80, "name": "1080P 高清" },
    { "id": 64, "name": "720P 高清" }
  ]
}
```

- `pages`：来自 `VInfo.PagesInfo`，`index` 从 1 开始，与 `SelectPage` 使用的编号一致。
- `qualities`：该视频当前凭据下实际可用的清晰度，`id` 为B站画质代号，`name` 取自 `Config.qualitys`（`MeowDown.Core/Config.cs:35-41`）。`name` 即 `DfnPriority` 可填的值。
- 空间投稿等批量类型**不支持** parse（仅下载），返回 `400 validation_failed`。

错误：`400 validation_failed`；`502 upstream_error`；`504 upstream_timeout`。同一用户同时只允许一个在途解析（重复提交返回 `429 rate_limited`）。

### `POST /api/tasks` — 提交下载任务

请求体 = `ServeRequestOptions` 的 JSON（与旧 `/add-task` 同构），并遵循以下 Web 模式规则：

- **分P 选择**：`SelectPage` 字符串，语法 `1,3,5-8,LAST`，或 `ALL`（`MeowDown/PageSelectionParser.cs`；非法返回 400）。
- **清晰度选择**：`DfnPriority` 为逗号分隔的清晰度名优先级列表（大小写不敏感，越靠前越优先，如 `"1080P 高清,720P 高清"`）。服务端按此优先级排序后取第一条轨（`Program.cs:872` + `SortTracks` `Program.cs:1290-1295`）；视频不提供首选清晰度时自动落到最优可用档。**锁定单一清晰度**即只填一项。名称必须与 `POST /api/parse` 返回的 `qualities[].name` 一致（可在 `DfnPriority` 追加未提供的档位作为回退）。
- **凭据**：Web 任务**禁止**自带 `Cookie` / `AccessToken`，填写即 `400 validation_failed`（服务端强制注入当前用户绑定的凭据）。
- **输出**：沿用服务器校验（`ServerRequestValidator`）：默认不允许 `WorkDir`、不允许绝对路径或含 `..` 的 `FilePattern`/`MultiFilePattern`；Web 模式下任务的工作目录强制为用户专属目录 `<server-download-root>/web-users/<userId>/`，即使服务器开启 `--server-allow-custom-output`，自定义 `WorkDir` 也一律拒绝。
- `CallBackWebHook`、`AudioLanguage`、`SubtitleLanguage`、`AiSubtitlePolicy`、`AudioOnly` 等字段语义与旧 API 一致（见 [json-api-doc.md](../json-api-doc.md)）。

请求示例：

```json
{
  "Url": "BV1qt4y1X7TW",
  "SelectPage": "1-3",
  "DfnPriority": "1080P 高清,720P 高清",
  "MultiThread": true
}
```

成功：`202 Accepted`

```json
{ "taskId": "e3b0c442..." }
```

错误：`400 validation_failed`（URL/分P/清晰度/受限字段等）；`401 unauthenticated`；`429 queue_full`（全局或该用户队列已满）。

### `GET /api/tasks` — 我的任务列表

返回当前用户的任务集合（按提交用户过滤）。响应结构沿用 `DownloadTaskCollection`（`pending`/`running`/`finished` 三列表），每个 `DownloadTask` 快照新增 `ownerId`、`ownerName` 字段（旧 API 消费方忽略新字段即可）。

`admin` 可传 `?scope=all` 查看全部用户任务（`user` 角色传 `scope=all` 返回 `403 forbidden`）。

成功：`200 OK`

```json
{
  "pending":  [ { "taskId": "...", "ownerId": "u_9f8a", "ownerName": "alice", "...": "同现有 DownloadTask" } ],
  "running":  [],
  "finished": []
}
```

`DownloadTask` 字段含义见 [json-api-doc.md](../json-api-doc.md) 数据结构章节；进度查询即读取其中 `Progress`（0–1）、`DownloadSpeed`、`TotalDownloadedBytes`、`IsSuccessful`、`Error`、`SavePaths`。

### `GET /api/tasks/{id}` — 单个任务详情/进度

`{id}` 为 `TaskId`（优先）或已解析的 AID。返回该用户的单个 `DownloadTask`（含 `ownerId`/`ownerName`）；不存在或不属于当前用户（且非 `admin`）返回 `404 not_found`。

### `DELETE /api/tasks/finished` — 清空我的已完成任务

仅删除当前用户自己的已完成任务。成功：`200 OK`。`admin` 可传 `?scope=all` 清空全部。

### `DELETE /api/tasks/finished/{id}` — 删除我的指定已完成任务

语义与旧 `/remove-finished/{id}` 相同，但限定本人任务。成功：`200 OK`（幂等，未找到也返回 200）。

> 与旧 API 一致：任务提交后无法取消单个任务（见 json-api-doc.md 注意事项）；任务存于内存，服务重启即清空。

---

## 端点：管理员

### `GET /api/admin/users` — 用户列表

成功：`200 OK`

```json
{
  "users": [
    {
      "id": "u_admin01",
      "username": "admin",
      "role": "admin",
      "disabled": false,
      "createdAt": 1769340000,
      "bilibiliBound": true,
      "bilibiliMid": "12345",
      "bilibiliUname": "某用户",
      "pendingTasks": 0,
      "runningTasks": 1
    }
  ]
}
```

### `POST /api/admin/users` — 新建用户

请求：

```json
{ "username": "bob", "password": "initial-pw", "role": "user" }
```

`role` 省略时为 `user`。成功：`201 Created`，响应体同用户列表中的单个元素。错误：`400 validation_failed`；`409 conflict`。

### `PATCH /api/admin/users/{id}` — 修改角色/停用

请求（字段均可选）：

```json
{ "role": "user", "disabled": true }
```

- `disabled=true` 立即吊销该用户全部会话，之后登录返回 `401 invalid_credentials`。
- 不允许对最后一个可用 `admin` 降级或停用（`409 conflict`）。

成功：`200 OK`，返回更新后的用户对象。

### `DELETE /api/admin/users/{id}` — 删除用户

- 不能删除自己（`409 conflict`）；不能删除最后一个 `admin`（`409 conflict`）。
- 级联：删除其B站凭据文件、吊销全部会话；其任务按正常保留策略自然淘汰，输出文件保留在磁盘。

成功：`204 No Content`。

### `POST /api/admin/users/{id}/password` — 重置密码

请求：

```json
{ "newPassword": "temp-pass-123" }
```

成功：`204 No Content`；吊销该用户全部会话；该用户下次登录 `passwordChangeRequired=true`。

### `GET /api/admin/config` — 读取系统配置

成功：`200 OK`

```json
{
  "writable": {
    "maxQueueLength": 100,
    "maxPendingTasksPerUser": 10,
    "maxFinishedTasks": 1000,
    "finishedTaskRetentionSeconds": 86400,
    "allowRegistration": false,
    "sessionLifetimeHours": 168
  },
  "readOnly": {
    "listenUrl": "http://0.0.0.0:23333",
    "downloadRoot": "/srv/meowdown/downloads",
    "webDataDir": "/srv/meowdown/web-data",
    "allowAria2cArgs": false,
    "allowCustomOutput": false,
    "allowCustomNetworkHosts": false,
    "allowPrivateCallbacks": false,
    "version": "2.2.0-beta5"
  }
}
```

### `PATCH /api/admin/config` — 修改系统配置（安全子集）

仅 `writable` 中的键可改，PATCH 传部分键即可。范围校验失败返回 `400 validation_failed`（附字段与允许范围）：

| 键 | 类型 | 范围 | 说明 |
| --- | --- | --- | --- |
| `maxQueueLength` | int | 1–10000 | 全局等待队列上限（同 `--server-max-queue`） |
| `maxPendingTasksPerUser` | int | 1–1000 | 单用户等待+执行任务上限 |
| `maxFinishedTasks` | int | 1–100000 | 已完成任务保留条数（同 `--server-max-finished`） |
| `finishedTaskRetentionSeconds` | int | 3600–2592000 | 已完成任务保留秒数（同 `--server-finished-retention-hours`） |
| `allowRegistration` | bool | — | 自助注册开关 |
| `sessionLifetimeHours` | int | 1–720 | 会话滑动有效期（对已有会话下次续期时生效） |

成功：`200 OK`，返回更新后的完整 config（同 GET 形状）。

`readOnly` 项（监听地址、下载根目录、各类 `allow*` 门禁、Token）均为启动期安全边界，**不可运行时修改**（对应 `MeowDownServerOptions`，`MeowDown/MeowDownServerOptions.cs:5-15`）。

---

## 静态文件与首页

- 静态文件由同一服务托管：`wwwroot/` 目录（`Microsoft.NET.Sdk.Web` 默认 Web 根），启用 `UseDefaultFiles` + `UseStaticFiles`。
- **首页路径：`GET /`** → `wwwroot/index.html`。
- 静态资源按扩展名 MIME 直出，均带 `Cache-Control: no-store` 以外的常规缓存策略（实现自定）；未命中路径返回 404；若前端使用 history 路由，可另加 `MapFallbackToFile("index.html")`（仅对非 `/api`、非旧 API 的 GET）。
- 前端全部通过同源 `/api/*` 调用；跨域访问 Web 会话 API 不被支持（CORS 不放行 `/api/*`）。

## 数据存储一览

| 数据 | 位置 | 说明 |
| --- | --- | --- |
| 用户账号（含口令哈希、角色） | `<web-data-dir>/web-users.json` | `WritePrivateFile` 原子写入、权限收紧 |
| 每用户B站凭据 | `<web-data-dir>/bili/<userId>.json` | cookie + refresh_token + mid/uname；同上 |
| 管理员可改的运行时配置 | `<web-data-dir>/web-settings.json` | 启动优先级：默认值 < web-settings.json < 命令行/MeowDown.config |
| Web 会话 | 内存 | 重启失效，需重新登录 |
| 绑定扫码会话 | 内存 | 180 秒有效期，随进程 |
| 下载任务 | 内存（沿用 `DownloadTaskStore`） | 重启清空，沿用保留策略 |

`<web-data-dir>` 默认 `<程序目录>/web-data/`，可用 `--web-data-dir` 修改（`Program.APP_DIR` 即程序目录，`Program.cs:33`）。

## 兼容性要点

1. 旧 Token API 路由、请求/响应、鉴权**零变更**；Token 不能访问 `/api/*`，Web 会话不能访问旧 API。
2. `DownloadTask` 快照新增 `ownerId`/`ownerName` 为纯增量字段。
3. Web 模式下的用户任务不使用服务器全局 Cookie（`MeowDown.data`）；仅使用各自绑定的凭据。全局 Cookie 继续作用于 CLI 与旧 Token API（若本地存在凭据文件）。
