
> 本项目仅供个人学习、研究和非商业性用途。使用本工具时，需自行确保遵守相关法律法规，特别是与版权相关的法律条款。开发者不对因使用本工具而产生的任何版权纠纷或法律责任承担责任。请谨慎使用，并仅在有合法授权的情况下使用相关内容。

# MeowDown
一个命令行式哔哩哔哩下载器. Bilibili Downloader.

> 传承链路：[BBDown](https://github.com/nilaoda/BBDown)（nilaoda，已归档）→ [BBDownT](https://github.com/LOVAHE/BBDownT)（LOVAHE）→ **MeowDown**（本分支）。MeowDown 以 BBDownT v2 为基线整合维护，项目名、命令和文档以 `MeowDown` 为准；向上游两代作者致谢。

# 注意
本软件混流时需要外部程序：

* 普通视频：[ffmpeg](https://www.gyan.dev/ffmpeg/builds/) ，或 [mp4box](https://gpac.wp.imt.fr/downloads/)
* 杜比视界：ffmpeg5.0以上或新版mp4box.

# 快速开始
从 [GitHub Releases](https://github.com/xvkong233/MeowDown/releases) 下载对应平台的压缩包，解压后即可直接运行。

独立二进制可运行 `MeowDown --update`（改名后如 `bbd --update`）更新到最新正式版。

# 迁移

如果你使用的是BBDown，并且想更新至MeowDown，不需要繁琐的重新配置，可以直接使用迁移命令完成一键迁移。

首先将MeowDown放到老的BBDown目录内，使用一键命令

```bash
MeowDown --migrate
```

（迁移时会跳过已存在的目标文件）迁移成功后，旧文件会改名为带 `.migrated-时间戳` 后缀的备份。

# 下载
Release 版本：https://github.com/xvkong233/MeowDown/releases —— 推送 v 前缀版本号标签（如 v2.2.0-beta4）自动构建并发布，带 beta/rc 后缀自动标记为预发布；标签必须与 csproj 的 Version 一致

上游 Release：https://github.com/LOVAHE/BBDownT/releases ｜ 上游构建产物：https://github.com/LOVAHE/BBDownT/actions/workflows/build_latest.yml

# 开始使用
目前命令行参数支持情况
```
Description:
  MeowDown是一个免费且便捷高效的哔哩哔哩下载/解析软件.

Usage:
  MeowDown <url> [command] [options]

Arguments:
  <url>  视频地址 或 av|bv|BV|ep|ss

Options:
  -tv, --use-tv-api                                            使用TV端解析模式
  -app, --use-app-api                                          使用APP端解析模式
  -intl, --use-intl-api                                        使用国际版(东南亚视频)解析模式
  --use-mp4box                                                 使用MP4Box来混流
  -e, --encoding-priority <encoding-priority>                  视频及音频编码的选择优先级, 用逗号分割 例: "hevc,av1,avc,flac,eac3,m4a"；与 -q 同时使用时越靠前越优先
  -q, --dfn-priority <dfn-priority>                            画质优先级,用逗号分隔 例: "8K 超高清, 1080P 高码率, HDR 真彩, 杜比视界"；与 -e 同时使用时越靠前越优先
  -info, --only-show-info                                      仅解析音视频和字幕信息，不下载；配合 --sub-only 仅列出字幕
  --show-all                                                   展示所有分P标题
  -aria2, --use-aria2c                                         调用aria2c进行下载(你需要自行准备好二进制可执行文件)
  -ia, --interactive                                           交互式选择音视频和字幕；字幕支持多选
  -hs, --hide-streams                                          不要显示所有可用音视频流
  -mt, --multi-thread                                          使用多线程下载(默认开启)
  --simply-mux                                                 精简混流，不增加描述、作者等信息
  --video-only                                                 仅下载视频；与 --audio-only 同时使用时下载两条独立主流并跳过混流，源不支持时失败
  --audio-only                                                 仅下载音频；与 --video-only 同时使用时下载两条独立主流并跳过混流，源不支持时失败
  --danmaku-only                                               仅下载弹幕
  --sub-only                                                   仅下载字幕
  --cover-only                                                 仅下载封面
  --skip-mux                                                   跳过混流步骤
  --skip-subtitle                                              跳过字幕下载
  --skip-cover                                                 跳过封面下载
  --force-http                                                 下载音视频时强制使用HTTP协议替换HTTPS（国内模式默认开启，国际模式默认关闭）
  -dd, --download-danmaku                                      下载弹幕
  -ddf, --download-danmaku-formats <download-danmaku-formats>  指定需下载的弹幕格式, 用逗号分隔, 可选 xml/ass, 默认: "xml,ass"
  --skip-ai                                                    跳过AI字幕下载(默认开启)
  --subtitle-language <subtitle-language>                      筛选字幕语言，逗号分隔；zh/en 匹配语言族，zh-Hans/ai-zh 精确匹配
  --ai-subtitle-policy <ai-subtitle-policy>                    字幕AI策略: exclude/include/prefer-human/only；显式指定时优先于 --skip-ai
  --video-ascending                                            视频升序(最小体积优先)
  --audio-ascending                                            音频升序(最小体积优先)
  --allow-pcdn                                                 不替换PCDN域名, 仅在正常情况与--upos-host均无法下载时使用
  -F, --file-pattern <file-pattern>                            使用内置变量自定义单P存储文件名:
  
                                                               <videoTitle>: 视频主标题
                                                               <pageNumber>: 视频分P序号
                                                               <pageNumberWithZero>: 视频分P序号(按总分P数位数补零)
                                                               <pageNumberWithZero:N>: 视频分P序号(固定至少N位, 1～10)
                                                               <pageTitle>: 视频分P标题
                                                               <bvid>: 视频BV号
                                                               <aid>: 视频aid
                                                               <cid>: 视频cid
                                                               <dfn>: 视频清晰度
                                                               <res>: 视频分辨率
                                                               <fps>: 视频帧率
                                                               <videoCodecs>: 视频编码
                                                               <videoBandwidth>: 视频码率
                                                               <audioCodecs>: 音频编码
                                                               <audioBandwidth>: 音频码率
                                                               <ownerName>: 上传者名称
                                                               <ownerMid>: 上传者mid
                                                               <publishDate>: 收藏夹/番剧/合集发布时间
                                                               <videoDate>: 视频发布时间(分p视频发布时间与<publishDate>相同)
                                                               <apiType>: API类型(TV/APP/INTL/WEB)
  
                                                               默认为: <videoTitle>
  -M, --multi-file-pattern <multi-file-pattern>                使用内置变量自定义多P存储文件名:
  
                                                               默认为: <videoTitle>/[P<pageNumberWithZero:2>]<pageTitle>
  -p, --select-page <select-page>                              选择指定分p或分p范围: (-p 8 或 -p 1,2 或 -p 3-5 或 -p ALL 或 -p LAST 或 -p 3,5,LATEST)
  --language <language>                                        设置混流的音频语言(代码), 如chi, jpn等
  --audio-language <audio-language>                            选择配音语言，精确匹配 -info 列出的代码；仅默认WEB/DASH模式，输出文件附加语言后缀
  -ua, --user-agent <user-agent>                               指定user-agent；登录态默认使用持久化浏览器配置，匿名默认使用随机user-agent
  cookie, -c <cookie>                                          设置字符串cookie用以下载网页接口的会员内容
  access-token, -token <access-token>                          设置access_token用以下载TV/APP接口的会员内容
  --aria2c-args <aria2c-args>                                  调用aria2c的附加参数(默认参数包含"-x16 -s16 -j16 -k 5M", 使用时注意字符串转义)
  --work-dir <work-dir>                                        设置程序的工作目录
  --ffmpeg-path <ffmpeg-path>                                  设置ffmpeg的路径
  --mp4box-path <mp4box-path>                                  设置mp4box的路径
  --aria2c-path <aria2c-path>                                  设置aria2c的路径
  --upos-host <upos-host>                                      自定义upos服务器
  --force-replace-host                                         强制替换下载服务器host(默认开启)
  --save-archives-to-file                                      按视频和分P记录下载结果，用于后续跳过已完成的分P
  --delay-per-page <delay-per-page>                            设置下载合集分P之间的下载间隔时间(单位: 秒, 默认无间隔)
  --download-all                                               导出UP主空间TXT清单后，按清单逐条下载全部投稿
  --delay-per-video <delay-per-video>                          批量下载时视频之间的间隔秒数，默认10，0为不等待
  --host <host>                                                指定BiliPlus host(使用BiliPlus需要access_token, 不需要cookie, 解析服务器能够获取你账号的大部分权限!)
  --ep-host <ep-host>                                          指定BiliPlus EP host(用于代理api.bilibili.com/pgc/view/web/season, 大部分解析服务器不支持代理该接口)
  --tv-host <tv-host>                                          自定义tv端接口请求Host(用于代理api.snm0516.aisee.tv)
  --area <area>                                                (hk|tw|th) 使用BiliPlus时必选, 指定BiliPlus area
  --config-file <config-file>                                  读取指定的MeowDown本地配置文件(默认为: MeowDown.config)
  --debug                                                      输出调试日志
  --update                                                     将独立可执行程序更新到最新版本（测试版可更新到同版本号的后续测试版）；需单独使用
  --migrate                                                    手动迁移程序目录中的旧版BBDown配置、登录文件和下载记录
  --api-token <api-token>                                      服务器API鉴权Token，监听非本机地址且未配置时会自动生成
  -?, -h, --help                                               Show help and usage information
  --version                                                    Show version information

Commands:
  login      通过APP扫描二维码以登录您的WEB账号
  logintv    通过APP扫描二维码以登录您的TV账号
  loginintl  登录国际站 BiliBili（Bstation），默认使用国际版 App 扫码
  serve      以服务器模式运行
```

# 功能
- [x] 番剧下载(Web|TV|App)
- [x] 课程下载(Web)
- [x] 普通内容下载(Web|TV|App)
- [x] 合集/列表/收藏夹/个人空间解析与下载
- [x] 多分P自动下载
- [x] 选择指定分P进行下载
- [x] 选择指定清晰度进行下载
- [x] 下载外挂字幕并转换为srt格式
- [x] 自动合并音频+视频流+字幕流+**章节信息**`(使用ffmpeg或mp4box)`
- [x] 单独下载视频/音频/字幕
- [x] 二维码登录账号
- [x] 多线程下载
- [x] 支持调用aria2c下载
- [x] 支持AVC/HEVC/AV1编码
- [x] **支持8K/HDR/HDR Vivid/杜比视界/杜比全景声下载**
- [x] 自定义存储文件名
- [x] 自动刷新cookie

# TODO
- [ ] 支持更多自定义选项

# 使用教程

<details>
<summary>配置文件 (NEW)</summary>

---

在`1.4.9`或更高版本中，MeowDown支持读取本地配置文件以简化命令行的手动输入。

如果没有指定`--config-file`，则默认读取程序同目录下的`MeowDown.config`文件；若指定该参数，则读取对应文件。

普通启动只读取MeowDown自己的配置文件，不会自动探测、迁移旧版BBDown的配置、登录文件或下载归档。

一个典型的配置文件:
```config
#本文件是MeowDown程序的配置文件
#以#开头的都会被程序忽略
#然后剩余非空白内容程序逐行读取，对于一个选项，其参数应当在下一行出现

#例如下面将设置输出文件名格式
--file-pattern
<videoTitle>[<dfn>]

--multi-file-pattern
<videoTitle>/[P<pageNumberWithZero>]<pageTitle>[<dfn>]

#下面设置下载多个分P时，每个分P的下载间隔为2秒
--delay-per-page
2

#开启弹幕下载功能
--download-danmaku
```

</details>

<details>
<summary>迁移旧版配置和登录信息</summary>

---

如果需要继续使用旧版 BBDown 的配置、登录信息或下载归档，请单独执行：
```
MeowDown --migrate
```

迁移程序所在目录中的文件，映射关系如下：

| 旧文件 | 新文件 |
| --- | --- |
| `BBDown.config` | `MeowDown.config` |
| `BBDown.data` | `MeowDown.data` |
| `BBDownTV.data` | `MeowDownTV.data` |
| `BBDownApp.data` | `MeowDownApp.data` |
| `BBDown.archives` | `MeowDown.archives` |
| `BBDownT.config` | `MeowDown.config` |
| `BBDownT.data` | `MeowDown.data` |
| `BBDownTTV.data` | `MeowDownTV.data` |
| `BBDownTApp.data` | `MeowDownApp.data` |
| `BBDownTIntl.data` | `MeowDownIntl.data` |
| `BBDownT.archives` | `MeowDown.archives` |

如果目标文件已经存在，程序会跳过该项，不会覆盖目标文件。迁移成功后，原文件会改名为带有`.migrated-时间戳`后缀的备份文件。迁移过程中出现错误时，命令会返回非零状态。

`--migrate`必须单独使用，不能与视频地址或其他参数同时使用。

</details>

启用 `--save-archives-to-file` 后，新记录使用 `aid:cid`，每个分P独立归档。旧的 aid 记录保留：仅在原始视频页列表确认该 aid 只有一个分P时沿用；多P或页数未知时逐页检查，不把旧记录视为所有分P均已完成。只选一个分P也不会改变原始视频的页数判断。

<details>
<summary>自定义输出文件名格式 (NEW)</summary> 

---

在`1.4.9`或更高版本中，MeowDown支持自定义合并时的文件名组成。
|  代码   | 含义  |
|  ----  | ----  |
`<videoTitle>`|视频主标题
`<pageNumber>`|视频分P序号
`<pageNumberWithZero>`|视频分P序号(前缀补零)
`<pageNumberWithZero:N>`|视频分P序号(固定至少N位, 1～10)
`<pageTitle>`|视频分P标题
`<bvid>`|视频BV号
`<aid>`|视频aid
`<cid>`|视频cid
`<dfn>`|视频清晰度
`<res>`|视频分辨率
`<fps>`|视频帧率
`<videoCodecs>`|视频编码
`<videoBandwidth>`|视频码率
`<audioCodecs>`|音频编码
`<audioBandwidth>`|音频码率
`<ownerName>`|上传者名称(下载番剧时，该值为"")
`<ownerMid>`|上传者mid(下载番剧时，该值为"")
`<publishDate>`|发布时间(yyyy-MM-dd_HH-mm-ss)
`<apiType>`|API类型（TV/APP/INTL/WEB）

</details>

<details>
<summary>WEB/TV鉴权</summary>  

---
  
扫码登录网页账号：
```
MeowDown login
```
然后按照提示操作。登录成功后会同时保存用于刷新Cookie的`ac_time_value`，后续运行时会在需要时自动刷新本地Cookie。

扫码登录云视听小电视账号：
```
MeowDown logintv
```
然后按照提示操作
 

扫码登录国际站账号（Bstation，东南亚版）：
```
MeowDown loginintl
```
然后按照提示操作。也可以手动输入并保存国际站 Cookie（输入不回显），`--cookie` 与 `--import-cookie` 等价：
```
MeowDown loginintl --import-cookie
```

*PS: 如果登录报错`The type initializer for 'Gdip' threw an exception`，通常是运行环境缺少图形或二维码相关依赖，请按当前系统补齐依赖后重试*

手动加载网页cookie：
```
MeowDown -c "SESSDATA=******" "https://www.bilibili.com/video/BV1qt4y1X7TW"
```
如需手动Cookie也支持自动刷新，请同时包含`bili_jct`和`ac_time_value`。旧版本生成的`MeowDown.data`如果缺少`ac_time_value`，需要重新执行一次`MeowDown login`。

手动加载云视听小电视token：
```
MeowDown -tv -token "******" "https://www.bilibili.com/video/BV1qt4y1X7TW"
```

</details>

<details>
<summary>APP鉴权</summary>  

---

> TV登录产生的`access_token`也可以给APP接口使用。可复制`MeowDownTV.data`到`MeowDownApp.data`使程序自动读取.

目前程序无法自动获取鉴权信息，推荐通过**抓包**来获取.

在请求Header中寻找键为`authorization`的项，其值形为`identify_v1 5227************1`，其中的`5227************1`就是token(access_key)

获取后手动通过`-token`命令加载, 或写入`MeowDownApp.data`使程序自动读取.
  
```
MeowDown -app -token "******" "https://www.bilibili.com/video/BV1qt4y1X7TW"
```

</details>

<details>
<summary>常用命令</summary>  

---

下载普通视频：
```
MeowDown "https://www.bilibili.com/video/BV1qt4y1X7TW"
```
使用TV接口下载(粉丝量大的UP主基本上是无水印片源)：
```
MeowDown -tv "https://www.bilibili.com/video/BV1qt4y1X7TW"
```
当分P过多时，默认会隐藏展示全部的分P信息，你可以使用如下命令来显示所有每一个分P。
```
MeowDown --show-all "https://www.bilibili.com/video/BV1At41167aj"
```
选择下载某些分P的三种情况：
* 单个分P：10
```
MeowDown "https://www.bilibili.com/video/BV1At41167aj?p=10"
MeowDown -p 10 "https://www.bilibili.com/video/BV1At41167aj"
```
* 多个分P：1,2,10
```
MeowDown -p 1,2,10 "https://www.bilibili.com/video/BV1At41167aj"
```
* 范围分P：1-10
```
MeowDown -p 1-10 "https://www.bilibili.com/video/BV1At41167aj"
```
下载番剧全集：
```
MeowDown -p ALL "https://www.bilibili.com/bangumi/play/ss33073"
```

</details>

<details>
<summary>配音语言选择</summary>

查看视频当前及可选的配音语言。配音列表位于音视频流列表之后，默认版本标记为 `[默认]` 并在终端中以绿色显示；AI 版本标记为 `[AI]`，显式选择其他版本时另标记 `[当前]`：

```bash
MeowDown -info "BVxxxx"
```

如果列表中包含 `en-US`，可以选择这个版本，或仅保存它的音频：

```bash
MeowDown --audio-language en-US "BVxxxx"
MeowDown --audio-language en-US --audio-only "BVxxxx"
```

- 语言代码以该视频的 `-info` 列表为准，上面的 `en-US` 只是示例。一次选择一个代码，不区分大小写，但必须完整匹配；`en` 不会自动匹配 `en-US`。
- 不指定时沿用接口默认版本；需要原声时，选择列表中对应原声的代码。此功能使用平台已有配音，不生成翻译或配音。
- 目前仅支持默认 WEB 模式下的 DASH 音视频流，不能与 `-tv`、`-app`、`-intl`、`--sub-only`、`--cover-only`、`--danmaku-only` 一起使用。
- 找不到语言或接口未确认返回该版本时直接报错，不会静默改回默认版本。选择后重新获取整套音视频地址，视频画面也可能随平台翻译版本变化。
- 显式选择会在最终输出及主音视频临时文件名中加入后缀，如 `视频.audio-en-us.mp4` 或 `视频.audio-en-us.m4a`，防止和其他配音版本混淆。
- 配合 `--save-archives-to-file` 时，显式配音版本不读写默认配音的下载归档；常规混流模式由带语言后缀的输出文件判断是否已下载，`--skip-mux` 仍走已有原始流下载流程。
- `--language` 仍只设置封装文件的音轨语言标签，例如 `--language eng`。字幕仍由 `--subtitle-language` 和 AI 字幕策略选择，不会因切换配音自动改选。
- 参数也可写入配置文件。`-ia` 仍选择音视频规格，配音语言通过 `--audio-language` 指定。

</details>

<details>
<summary>字幕选择</summary>

国内视频字幕使用新版字幕接口，自动解析编码后的字幕地址，不再回退到旧版播放器、视频详情或 gRPC 字幕接口。国际站继续使用其独立字幕接口。

---

查看可用字幕及筛选结果，不下载字幕文件：
```
MeowDown -info --sub-only "https://www.bilibili.com/video/BV1qt4y1X7TW"
```

下载中英文字幕，同语言优先选择普通(CC)字幕，没有时保留AI字幕：
```
MeowDown --sub-only --subtitle-language zh,en --ai-subtitle-policy prefer-human "https://www.bilibili.com/video/BV1qt4y1X7TW"
```

`--subtitle-language`用逗号分隔多个语言，不区分大小写。`zh`、`en`会匹配同一语言族的字幕，例如`ai-zh`、`en-US`；`zh-Hans`、`ai-zh`等带连字符的代码只匹配对应语言。不指定时不限制字幕语言。

`--ai-subtitle-policy`支持以下选项：

| 选项 | 含义 |
| --- | --- |
| `exclude` | 排除AI字幕 |
| `include` | 包含AI字幕 |
| `prefer-human` | 同语言族有普通(CC)字幕时跳过AI字幕，否则保留AI字幕 |
| `only` | 仅下载AI字幕 |

不指定策略时沿用`--skip-ai`，默认跳过AI字幕；显式指定策略时优先使用该策略。语言和策略参数也可以写入配置文件。

手动选择需要下载的字幕，并允许AI字幕出现在候选中：
```
MeowDown --sub-only -ia --skip-ai false "https://www.bilibili.com/video/BV1qt4y1X7TW"
```
然后按照提示输入从1开始的字幕序号，支持逗号列表、范围、`ALL`或`NONE`。直接回车保留所有候选，输入错误会重新询问。使用`-info`时只显示列表，不进行交互。

`-p`、文件名模板和`--skip-subtitle`同样适用；没有符合条件的字幕时会给出提示。同语言存在多条字幕时，文件名会增加轨道标识，避免互相覆盖。

*PS: 程序先筛选语言，再应用AI策略，因此指定`zh-Hans`不会自动改选`ai-zh`。普通(CC)是平台分类，不保证人工校对；来源未知的字幕在`prefer-human`策略下会保留，但不会替代AI字幕。*

</details>

<details>
<summary>下载个人空间全部投稿</summary>

---

直接输入UP主的空间地址，可以导出该UP主的全部投稿视频地址：
```
MeowDown "https://space.bilibili.com/123456"
```
视频地址会保存到工作目录下的`UP主名称的投稿视频.txt`文件中。

如果你需要自动下载这些视频，可以加上`--download-all`：
```
MeowDown --download-all "https://space.bilibili.com/123456"
```
程序会先保存完整的视频地址文件，保存成功后再按顺序逐个下载。

默认每个视频下载完成后等待10秒再下载下一个，你可以使用如下命令调整间隔：
```
MeowDown --download-all --delay-per-video 15 "https://space.bilibili.com/123456"
```
将`--delay-per-video`设为`0`可以取消等待。如果一个视频有多个分P，分P之间的间隔仍然使用`--delay-per-page`设置。

其他下载参数也可以一起使用，例如只下载音频，并保存到指定目录：
```
MeowDown --download-all --audio-only --work-dir "./downloads" "https://space.bilibili.com/123456"
```

如果某个视频下载失败，程序会等待指定间隔后继续下载下一个，最后统一提示失败数量。有失败项时，命令会返回失败状态。

*PS: 这里只会读取本次生成的txt文件，暂不支持直接传入已有的txt文件。与`--only-show-info`同时使用时只导出视频地址。*

</details>

<details>
<summary>API服务器</summary>

启动服务器（自定义监听地址和端口）：

```shell
MeowDown serve -l http://0.0.0.0:12450
```

API服务器不支持HTTPS配置，如果有需要请自行使用nginx等反向代理进行配置

API详细请参考[json-api-doc.md](./json-api-doc.md)

</details>

# Web 服务部署

`serve` 模式内置多用户 Web 服务：同源托管网页前端（登录/注册、在线解析与下载、B站扫码绑定、管理员面板），API 契约见 [docs/web-api.md](./docs/web-api.md)。推荐使用 Docker Compose 一键部署（仓库根目录的 `Dockerfile` + `docker-compose.yml`）：

```bash
# 构建镜像并启动
docker compose up -d --build

# 获取管理员初始密码（仅首次启动打印一次）
docker compose logs meowdown | grep "已创建Web管理员账号"
```

启动后浏览器访问 `http://<服务器IP>:23333` 即可使用。

## 默认账号

首次启动时 Web 用户库为空，服务端自动引导创建管理员账号：

- 用户名固定为 `admin`，角色 `admin`。
- 初始密码优先取启动参数 `--web-admin-password`（可在 `docker-compose.yml` 中取消 `command` 注释并填写）。
- 未配置该参数时自动生成 16 位随机密码，**仅在服务器日志打印一次**，格式为 `已创建Web管理员账号: admin 初始密码: <password>`，请用 `docker compose logs` 查看并立即登录修改；修改密码前，每次登录响应都带 `passwordChangeRequired: true` 提示。
- 自助注册默认关闭，管理员可在网页管理面板开启，或启动时加 `--web-allow-registration`。

## 数据持久化

数据均落在 compose 挂载的宿主机目录（`./data/`），升级或重建容器不丢失：

| 宿主机目录 | 容器目录 | 内容 |
| --- | --- | --- |
| `./data/web-data` | `/data/web-data` | Web 用户账号（`web-users.json`）、每用户B站绑定凭据（`bili/`）、运行时配置（`web-settings.json`） |
| `./data/downloads` | `/data/downloads` | 下载输出根目录，Web 任务按用户存放于 `web-users/<userId>/` |

## HTTPS 与安全

- 服务本身只提供 HTTP，HTTPS 请由反向代理（nginx/caddy 等）终止，并为容器追加 `--web-secure-cookie` 参数（会话 Cookie 附加 `Secure`），或由反代还原 `X-Forwarded-Proto`。
- 旧 Token API（`/add-task` 等）在容器内同样可用：监听非本机地址且未配置 `--api-token` 时会自动生成并打印到日志（与 Web 会话互不通用）。

# 演示
![1](https://user-images.githubusercontent.com/20772925/88686407-a2001480-d129-11ea-8aac-97a0c71af115.gif)

下载完毕后在当前目录查看MP4文件：

![2](https://user-images.githubusercontent.com/20772925/88478901-5e1cdc00-cf7e-11ea-97c1-154b9226564e.png)

# 致谢

* https://github.com/nilaoda/BBDown
* https://github.com/bggRGjQaUbCoE/PiliPlus
* https://github.com/Shane32/QRCoder
* https://github.com/icsharpcode/SharpZipLib
* https://github.com/protocolbuffers/protobuf
* https://github.com/grpc/grpc
* https://github.com/dotnet/command-line-api
* https://github.com/FFmpeg/FFmpeg
* https://github.com/gpac/gpac
* https://github.com/aria2/aria2
