using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeowDown.Core;
using MeowDown.Core.Util;
using static MeowDown.Core.Logger;
using static MeowDown.MeowDownUtil;

namespace MeowDown;

internal static class CommandLineInvoker
{
    //GA 默认开启 POSIX 短选项捆绑，保持与旧版一致的逐 token 解析行为
    internal static readonly ParserConfiguration ParsingConfiguration = new() { EnablePosixBundling = false };

    //GA 的 InvocationConfiguration 会在首次使用时缓存 Console.Out/Error，必须每次调用新建，避免缓存已被释放的重定向 writer
    internal static InvocationConfiguration CreateInvocationConfiguration() => new();

    //退出动作经此钩子注入，默认直接退出进程；测试可替换以便在进程内存活断言
    internal static Action<int> ExitHook { get; set; } = Environment.Exit;

    private static readonly Argument<string> Url = new("url") { Description = "视频地址 或 av|bv|BV|ep|ss" };
    private static readonly Option<bool> UseTvApi = new("--use-tv-api", "-tv") { Description = "使用TV端解析模式" };
    private static readonly Option<bool> UseAppApi = new("--use-app-api", "-app") { Description = "使用APP端解析模式" };
    private static readonly Option<bool> UseIntlApi = new("--use-intl-api", "-intl") { Description = "使用国际版(东南亚视频)解析模式" };
    private static readonly Option<bool> UseMP4box = new("--use-mp4box") { Description = "使用MP4Box来混流" };
    private static readonly Option<string> EncodingPriority = new("--encoding-priority", "-e") { Description = "视频及音频编码的选择优先级, 用逗号分割 例: \"hevc,av1,avc,flac,eac3,m4a\"；与 -q 同时使用时越靠前越优先" };
    private static readonly Option<string> DfnPriority = new("--dfn-priority", "-q") { Description = "画质优先级,用逗号分隔 例: \"8K 超高清, 1080P 高码率, HDR 真彩, 杜比视界\"；与 -e 同时使用时越靠前越优先" };
    private static readonly Option<bool> OnlyShowInfo = new("--only-show-info", "-info") { Description = "仅解析音视频和字幕信息，不下载；配合 --sub-only 仅列出字幕" };
    private static readonly Option<bool> HideStreams = new("--hide-streams", "-hs") { Description = "不要显示所有可用音视频流" };
    private static readonly Option<bool> Interactive = new("--interactive", "-ia") { Description = "交互式选择音视频和字幕；字幕支持多选" };
    private static readonly Option<bool> ShowAll = new("--show-all") { Description = "展示所有分P标题" };
    private static readonly Option<bool> UseAria2c = new("--use-aria2c", "-aria2") { Description = "调用aria2c进行下载(你需要自行准备好二进制可执行文件)" };
    private static readonly Option<string> Aria2cArgs = new("--aria2c-args") { Description = "调用aria2c的附加参数(默认参数包含\"-x16 -s16 -j16 -k 5M\", 使用时注意字符串转义)" };
    private static readonly Option<bool> MultiThread = new("--multi-thread", "-mt") { Description = "使用多线程下载(默认开启)" };
    private static readonly Option<string> SelectPage = new("--select-page", "-p") { Description = "选择指定分p或分p范围: (-p 8 或 -p 1,2 或 -p 3-5 或 -p ALL 或 -p LAST 或 -p 3,5,LATEST)" };
    private static readonly Option<bool> SimplyMux = new("--simply-mux") { Description = "精简混流，不增加描述、作者等信息" };
    private static readonly Option<bool> AudioOnly = new("--audio-only") { Description = "仅下载音频；与 --video-only 同时使用时下载两条独立主流并跳过混流，源不支持时失败" };
    private static readonly Option<bool> VideoOnly = new("--video-only") { Description = "仅下载视频；与 --audio-only 同时使用时下载两条独立主流并跳过混流，源不支持时失败" };
    private static readonly Option<bool> DanmakuOnly = new("--danmaku-only") { Description = "仅下载弹幕" };
    private static readonly Option<bool> CoverOnly = new("--cover-only") { Description = "仅下载封面" };
    private static readonly Option<bool> SubOnly = new("--sub-only") { Description = "仅下载字幕" };
    internal static readonly Option<bool> Debug = new("--debug") { Description = "输出调试日志", Recursive = true };
    private static readonly Option<bool> SkipMux = new("--skip-mux") { Description = "跳过混流步骤" };
    private static readonly Option<bool> SkipSubtitle = new("--skip-subtitle") { Description = "跳过字幕下载" };
    private static readonly Option<bool> SkipCover = new("--skip-cover") { Description = "跳过封面下载" };
    private static readonly Option<bool> ForceHttp = new("--force-http") { Description = "下载音视频时强制使用HTTP协议替换HTTPS（国内模式默认开启，国际模式默认关闭）" };
    private static readonly Option<bool> DownloadDanmaku = new("--download-danmaku", "-dd") { Description = "下载弹幕" };
    private static readonly Option<string> DownloadDanmakuFormats = new("--download-danmaku-formats", "-ddf") { Description = $"指定需下载的弹幕格式, 用逗号分隔, 可选 {string.Join('/', MeowDownDanmakuFormatInfo.AllFormatNames)}, 默认: \"{string.Join(',', MeowDownDanmakuFormatInfo.DefaultFormatsNames)}\"" };
    private static readonly Option<bool> SkipAi = new("--skip-ai") { Description = "跳过AI字幕下载(默认开启)" };
    private static readonly Option<string> SubtitleLanguage = new("--subtitle-language") { Description = "筛选字幕语言，逗号分隔；zh/en 匹配语言族，zh-Hans/ai-zh 精确匹配" };
    private static readonly Option<string> AiSubtitlePolicy = new("--ai-subtitle-policy") { Description = "字幕AI策略: exclude/include/prefer-human/only；显式指定时优先于 --skip-ai" };

    static CommandLineInvoker()
    {
        AudioLanguage.Validators.Add(result =>
        {
            if (AudioLanguageSelection.ValidateCode(result.GetValueOrDefault<string>()) is { } codeError) result.AddError(codeError);
        });
        SubtitleLanguage.Validators.Add(result =>
        {
            if (SubtitleSelection.ValidateLanguage(result.GetValueOrDefault<string>()) is { } languageError) result.AddError(languageError);
        });
        AiSubtitlePolicy.Validators.Add(result =>
        {
            if (SubtitleSelection.ValidatePolicy(result.GetValueOrDefault<string>()) is { } policyError) result.AddError(policyError);
        });
    }
    private static readonly Option<bool> VideoAscending = new("--video-ascending") { Description = "视频升序(最小体积优先)" };
    private static readonly Option<bool> AudioAscending = new("--audio-ascending") { Description = "音频升序(最小体积优先)" };
    private static readonly Option<bool> AllowPcdn = new("--allow-pcdn") { Description = "不替换PCDN域名, 仅在正常情况与--upos-host均无法下载时使用" };
    private static readonly Option<string> Language = new("--language") { Description = "设置混流的音频语言(代码), 如chi, jpn等" };
    private static readonly Option<string> AudioLanguage = new("--audio-language") { Description = "选择配音语言，精确匹配 -info 列出的代码；仅默认WEB/DASH模式，输出文件附加语言后缀" };
    private static readonly Option<string> UserAgent = new("--user-agent", "-ua") { Description = "指定user-agent；登录态默认使用持久化浏览器配置，匿名默认使用随机user-agent" };
    //GA 下 Name 保留声明原样且 Aliases 不含主名；cookie/access-token 以无杠主名+显式长别名声明，保持与旧版一致的别名集合
    private static readonly Option<string> Cookie = new("cookie", "--cookie", "-c") { Description = "设置字符串cookie用以下载网页接口的会员内容" };
    private static readonly Option<string> AccessToken = new("access-token", "--access-token", "-token") { Description = "设置access_token用以下载TV/APP接口的会员内容" };
    private static readonly Option<string> WorkDir = new("--work-dir") { Description = "设置程序的工作目录" };
    private static readonly Option<string> FFmpegPath = new("--ffmpeg-path") { Description = "设置ffmpeg的路径" };
    private static readonly Option<string> Mp4boxPath = new("--mp4box-path") { Description = "设置mp4box的路径" };
    private static readonly Option<string> Aria2cPath = new("--aria2c-path") { Description = "设置aria2c的路径" };
    private static readonly Option<string> UposHost = new("--upos-host") { Description = "自定义upos服务器" };
    private static readonly Option<bool> ForceReplaceHost = new("--force-replace-host") { Description = "强制替换下载服务器host(默认开启)" };
    private static readonly Option<bool> SaveArchivesToFile = new("--save-archives-to-file") { Description = "按视频和分P记录下载结果，用于后续跳过已完成的分P" };
    private static readonly Option<string> DelayPerPage = new("--delay-per-page") { Description = "设置下载合集分P之间的下载间隔时间(单位: 秒, 默认无间隔)" };
    private static readonly Option<bool> DownloadAll = new("--download-all") { Description = "导出UP主空间TXT清单后，按清单逐条下载全部投稿" };
    private static readonly Option<int> DelayPerVideo = new("--delay-per-video") { Description = "批量下载时视频之间的间隔秒数，默认10，0为不等待" };
    private static readonly Option<string> FilePattern = new("--file-pattern", "-F")
    {
        Description =
        $"使用内置变量自定义单P存储文件名:\r\n\r\n" +
        $"<videoTitle>: 视频主标题\r\n" +
        $"<pageNumber>: 视频分P序号\r\n" +
        $"<pageNumberWithZero>: 视频分P序号(按总分P数位数补零)\r\n" +
        $"<pageNumberWithZero:N>: 视频分P序号(固定至少N位, 1～10)\r\n" +
        $"<pageTitle>: 视频分P标题\r\n" +
        $"<bvid>: 视频BV号\r\n" +
        $"<aid>: 视频aid\r\n" +
        $"<cid>: 视频cid\r\n" +
        $"<dfn>: 视频清晰度\r\n" +
        $"<res>: 视频分辨率\r\n" +
        $"<fps>: 视频帧率\r\n" +
        $"<videoCodecs>: 视频编码\r\n" +
        $"<videoBandwidth>: 视频码率\r\n" +
        $"<audioCodecs>: 音频编码\r\n" +
        $"<audioBandwidth>: 音频码率\r\n" +
        $"<ownerName>: 上传者名称\r\n" +
        $"<ownerMid>: 上传者mid\r\n" +
        $"<publishDate>: 收藏夹/番剧/合集发布时间\r\n" +
        $"<videoDate>: 视频发布时间(分p视频发布时间与<publishDate>相同)\r\n" +
        $"<apiType>: API类型(TV/APP/INTL/WEB)\r\n\r\n" +
        $"默认为: {Program.SinglePageDefaultSavePath}\r\n"
    };
    private static readonly Option<string> MultiFilePattern = new("--multi-file-pattern", "-M") { Description = $"使用内置变量自定义多P存储文件名:\r\n\r\n默认为: {Program.MultiPageDefaultSavePath}\r\n" };
    private static readonly Option<string> Host = new("--host") { Description = "指定BiliPlus host(使用BiliPlus需要access_token, 不需要cookie, 解析服务器能够获取你账号的大部分权限!)" };
    private static readonly Option<string> EpHost = new("--ep-host") { Description = "指定BiliPlus EP host(用于代理api.bilibili.com/pgc/view/web/season, 大部分解析服务器不支持代理该接口)" };
    private static readonly Option<string> TvHost = new("--tv-host") { Description = "自定义tv端接口请求Host(用于代理api.snm0516.aisee.tv)" };
    private static readonly Option<string> Area = new("--area") { Description = "(hk|tw|th) 使用BiliPlus时必选, 指定BiliPlus area" };
    internal static readonly Option<string> ConfigFile = new("--config-file") { Description = "读取指定的MeowDown本地配置文件(默认为: MeowDown.config)", Recursive = true };//以下仅为兼容旧版本命令行, 不建议使用
    private static readonly Option<string> Aria2cProxy = new("--aria2c-proxy") { Description = "调用aria2c进行下载时的代理地址配置", Hidden = true };
    private static readonly Option<bool> OnlyHevc = new("--only-hevc", "-hevc") { Description = "只下载hevc编码", Hidden = true };
    private static readonly Option<bool> OnlyAvc = new("--only-avc", "-avc") { Description = "只下载avc编码", Hidden = true };
    private static readonly Option<bool> OnlyAv1 = new("--only-av1", "-av1") { Description = "只下载av1编码", Hidden = true };
    private static readonly Option<bool> AddDfnSubfix = new("--add-dfn-subfix") { Description = "为文件加入清晰度后缀, 如XXX[1080P 高码率]", Hidden = true };
    private static readonly Option<bool> NoPaddingPageNum = new("--no-padding-page-num") { Description = "不给分P序号补零", Hidden = true };
    private static readonly Option<bool> BandwithAscending = new("--bandwith-ascending") { Description = "比特率升序(最小体积优先)", Hidden = true };


    internal static MyOption MapFrom(ParseResult parseResult)
    {
        var option = new MyOption
        {
            Url = parseResult.GetValue(Url)!,
            EncodingPriorityFirst = CommandLineOptionOrder.EncodingPrecedesQuality(
                parseResult.Tokens.Select(token => token.Value))
        };

        if (parseResult.GetResult(UseTvApi) is { Implicit: false }) option.UseTvApi = parseResult.GetValue(UseTvApi);
        if (parseResult.GetResult(UseAppApi) is { Implicit: false }) option.UseAppApi = parseResult.GetValue(UseAppApi);
        if (parseResult.GetResult(UseIntlApi) is { Implicit: false }) option.UseIntlApi = parseResult.GetValue(UseIntlApi);
        if (parseResult.GetResult(UseMP4box) is { Implicit: false }) option.UseMP4box = parseResult.GetValue(UseMP4box);
        if (parseResult.GetResult(EncodingPriority) is { Implicit: false }) option.EncodingPriority = parseResult.GetValue(EncodingPriority)!;
        if (parseResult.GetResult(DfnPriority) is { Implicit: false }) option.DfnPriority = parseResult.GetValue(DfnPriority)!;
        if (parseResult.GetResult(OnlyShowInfo) is { Implicit: false }) option.OnlyShowInfo = parseResult.GetValue(OnlyShowInfo);
        if (parseResult.GetResult(ShowAll) is { Implicit: false }) option.ShowAll = parseResult.GetValue(ShowAll);
        if (parseResult.GetResult(UseAria2c) is { Implicit: false }) option.UseAria2c = parseResult.GetValue(UseAria2c);
        if (parseResult.GetResult(Interactive) is { Implicit: false }) option.Interactive = parseResult.GetValue(Interactive);
        if (parseResult.GetResult(HideStreams) is { Implicit: false }) option.HideStreams = parseResult.GetValue(HideStreams);
        if (parseResult.GetResult(MultiThread) is { Implicit: false }) option.MultiThread = parseResult.GetValue(MultiThread);
        if (parseResult.GetResult(SimplyMux) is { Implicit: false }) option.SimplyMux = parseResult.GetValue(SimplyMux);
        if (parseResult.GetResult(VideoOnly) is { Implicit: false }) option.VideoOnly = parseResult.GetValue(VideoOnly);
        if (parseResult.GetResult(AudioOnly) is { Implicit: false }) option.AudioOnly = parseResult.GetValue(AudioOnly);
        if (parseResult.GetResult(DanmakuOnly) is { Implicit: false }) option.DanmakuOnly = parseResult.GetValue(DanmakuOnly);
        if (parseResult.GetResult(CoverOnly) is { Implicit: false }) option.CoverOnly = parseResult.GetValue(CoverOnly);
        if (parseResult.GetResult(SubOnly) is { Implicit: false }) option.SubOnly = parseResult.GetValue(SubOnly);
        if (parseResult.GetResult(Debug) is { Implicit: false }) option.Debug = parseResult.GetValue(Debug);
        if (parseResult.GetResult(SkipMux) is { Implicit: false }) option.SkipMux = parseResult.GetValue(SkipMux);
        if (parseResult.GetResult(SkipSubtitle) is { Implicit: false }) option.SkipSubtitle = parseResult.GetValue(SkipSubtitle);
        if (parseResult.GetResult(SkipCover) is { Implicit: false }) option.SkipCover = parseResult.GetValue(SkipCover);
        if (parseResult.GetResult(ForceHttp) is { Implicit: false }) option.ForceHttp = parseResult.GetValue(ForceHttp);
        if (parseResult.GetResult(DownloadDanmaku) is { Implicit: false }) option.DownloadDanmaku = parseResult.GetValue(DownloadDanmaku);
        if (parseResult.GetResult(DownloadDanmakuFormats) is { Implicit: false }) option.DownloadDanmakuFormats = parseResult.GetValue(DownloadDanmakuFormats)!;
        if (parseResult.GetResult(SkipAi) is { Implicit: false }) option.SkipAi = parseResult.GetValue(SkipAi);
        if (parseResult.GetResult(SubtitleLanguage) is { Implicit: false }) option.SubtitleLanguage = parseResult.GetValue(SubtitleLanguage);
        if (parseResult.GetResult(AiSubtitlePolicy) is { Implicit: false }) option.AiSubtitlePolicy = parseResult.GetValue(AiSubtitlePolicy);
        if (parseResult.GetResult(VideoAscending) is { Implicit: false }) option.VideoAscending = parseResult.GetValue(VideoAscending);
        if (parseResult.GetResult(AudioAscending) is { Implicit: false }) option.AudioAscending = parseResult.GetValue(AudioAscending);
        if (parseResult.GetResult(AllowPcdn) is { Implicit: false }) option.AllowPcdn = parseResult.GetValue(AllowPcdn);
        if (parseResult.GetResult(FilePattern) is { Implicit: false }) option.FilePattern = parseResult.GetValue(FilePattern)!;
        if (parseResult.GetResult(MultiFilePattern) is { Implicit: false }) option.MultiFilePattern = parseResult.GetValue(MultiFilePattern)!;
        if (parseResult.GetResult(SelectPage) is { Implicit: false })
        {
            option.SelectPage = parseResult.GetValue(SelectPage)!;
            option.SelectPageSpecified = true;
        }
        if (parseResult.GetResult(Language) is { Implicit: false }) option.Language = parseResult.GetValue(Language)!;
        if (parseResult.GetResult(AudioLanguage) is { Implicit: false }) option.AudioLanguage = parseResult.GetValue(AudioLanguage);
        if (parseResult.GetResult(UserAgent) is { Implicit: false }) option.UserAgent = parseResult.GetValue(UserAgent)!;
        if (parseResult.GetResult(Cookie) is { Implicit: false }) option.Cookie = parseResult.GetValue(Cookie)!;
        if (parseResult.GetResult(AccessToken) is { Implicit: false }) option.AccessToken = parseResult.GetValue(AccessToken)!;
        if (parseResult.GetResult(Aria2cArgs) is { Implicit: false }) option.Aria2cArgs = parseResult.GetValue(Aria2cArgs)!;
        if (parseResult.GetResult(WorkDir) is { Implicit: false }) option.WorkDir = parseResult.GetValue(WorkDir)!;
        if (parseResult.GetResult(FFmpegPath) is { Implicit: false }) option.FFmpegPath = parseResult.GetValue(FFmpegPath)!;
        if (parseResult.GetResult(Mp4boxPath) is { Implicit: false }) option.Mp4boxPath = parseResult.GetValue(Mp4boxPath)!;
        if (parseResult.GetResult(Aria2cPath) is { Implicit: false }) option.Aria2cPath = parseResult.GetValue(Aria2cPath)!;
        if (parseResult.GetResult(UposHost) is { Implicit: false }) option.UposHost = parseResult.GetValue(UposHost)!;
        if (parseResult.GetResult(ForceReplaceHost) is { Implicit: false }) option.ForceReplaceHost = parseResult.GetValue(ForceReplaceHost)!;
        if (parseResult.GetResult(SaveArchivesToFile) is { Implicit: false }) option.SaveArchivesToFile = parseResult.GetValue(SaveArchivesToFile)!;
        if (parseResult.GetResult(DelayPerPage) is { Implicit: false }) option.DelayPerPage = parseResult.GetValue(DelayPerPage)!;
        if (parseResult.GetResult(DownloadAll) is { Implicit: false }) option.DownloadAll = parseResult.GetValue(DownloadAll);
        if (parseResult.GetResult(DelayPerVideo) is { Implicit: false }) option.DelayPerVideo = parseResult.GetValue(DelayPerVideo);
        if (parseResult.GetResult(Host) is { Implicit: false }) option.Host = parseResult.GetValue(Host)!;
        if (parseResult.GetResult(EpHost) is { Implicit: false }) option.EpHost = parseResult.GetValue(EpHost)!;
        if (parseResult.GetResult(TvHost) is { Implicit: false }) option.TvHost = parseResult.GetValue(TvHost)!;
        if (parseResult.GetResult(Area) is { Implicit: false }) option.Area = parseResult.GetValue(Area)!;
        if (parseResult.GetResult(Aria2cProxy) is { Implicit: false }) option.Aria2cProxy = parseResult.GetValue(Aria2cProxy)!;
        if (parseResult.GetResult(OnlyHevc) is { Implicit: false }) option.OnlyHevc = parseResult.GetValue(OnlyHevc)!;
        if (parseResult.GetResult(OnlyAvc) is { Implicit: false }) option.OnlyAvc = parseResult.GetValue(OnlyAvc)!;
        if (parseResult.GetResult(OnlyAv1) is { Implicit: false }) option.OnlyAv1 = parseResult.GetValue(OnlyAv1)!;
        if (parseResult.GetResult(AddDfnSubfix) is { Implicit: false }) option.AddDfnSubfix = parseResult.GetValue(AddDfnSubfix)!;
        if (parseResult.GetResult(NoPaddingPageNum) is { Implicit: false }) option.NoPaddingPageNum = parseResult.GetValue(NoPaddingPageNum)!;
        if (parseResult.GetResult(BandwithAscending) is { Implicit: false }) option.BandwithAscending = parseResult.GetValue(BandwithAscending)!;
        return option;
    }

    public static RootCommand GetRootCommand(Func<MyOption, Task> action)
    {
        var rootCommand = new RootCommand
        {
            Url,
            UseTvApi,
            UseAppApi,
            UseIntlApi,
            UseMP4box,
            EncodingPriority,
            DfnPriority,
            OnlyShowInfo,
            ShowAll,
            UseAria2c,
            Interactive,
            HideStreams,
            MultiThread,
            SimplyMux,
            VideoOnly,
            AudioOnly,
            DanmakuOnly,
            SubOnly,
            CoverOnly,
            SkipMux,
            SkipSubtitle,
            SkipCover,
            ForceHttp,
            DownloadDanmaku,
            DownloadDanmakuFormats,
            SkipAi,
            SubtitleLanguage,
            AiSubtitlePolicy,
            VideoAscending,
            AudioAscending,
            AllowPcdn,
            FilePattern,
            MultiFilePattern,
            SelectPage,
            Language,
            AudioLanguage,
            UserAgent,
            Cookie,
            AccessToken,
            Aria2cArgs,
            WorkDir,
            FFmpegPath,
            Mp4boxPath,
            Aria2cPath,
            UposHost,
            ForceReplaceHost,
            SaveArchivesToFile,
            DelayPerPage,
            DownloadAll,
            DelayPerVideo,
            Host,
            EpHost,
            TvHost,
            Area,
            Aria2cProxy,
            OnlyHevc,
            OnlyAvc,
            OnlyAv1,
            AddDfnSubfix,
            NoPaddingPageNum,
            BandwithAscending
        };

        rootCommand.Add(ConfigFile);
        rootCommand.Add(Debug);
        rootCommand.SetAction((Func<ParseResult, Task<int>>)(parseResult => RunGuarded(() => action(MapFrom(parseResult)))));

        return rootCommand;
    }

    internal static async Task<int> RunGuarded(Func<Task> body)
    {
        await RunGuarded(async () => { await body(); return 0; });
        return 0;
    }

    internal static async Task<int> RunGuarded(Func<Task<int>> body)
    {
        try
        {
            return await body();
        }
        catch (Exception ex)
        {
            LogError(Config.DEBUG_LOG ? RedactSensitiveText(ex.ToString()) : ErrorText.Describe(ex));
            try { Console.CursorVisible = true; } catch { }
            Thread.Sleep(3000);
            ExitHook(1);
            return 1;
        }
    }
}
