using System;
using System.IO;
using System.Linq;
using System.Net;

namespace BBDownT;

internal class ServerRequestValidator
{
    private readonly BBDownTServerOptions serverOptions;

    internal ServerRequestValidator(BBDownTServerOptions serverOptions)
    {
        this.serverOptions = serverOptions;
    }

    internal string? ValidateAndNormalizeServerRequest(ServeRequestOptions req)
    {
        req.RestrictedOutputRoot = null;
        if (string.IsNullOrWhiteSpace(req.Url))
        {
            return "Url不能为空";
        }

        req.Url = req.Url.Trim();
        var url = req.Url;
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !BBDownTUtil.IsBilibiliUrl(url))
        {
            return "Url只能是B站链接或av/BV/ep/ss等编号";
        }

        if (!string.IsNullOrWhiteSpace(req.FFmpegPath) || !string.IsNullOrWhiteSpace(req.Mp4boxPath)
            || !string.IsNullOrWhiteSpace(req.Aria2cPath))
        {
            return "服务器任务不能指定FFmpegPath、Mp4boxPath或Aria2cPath，请在服务器上安装这些程序或放到BBDownT程序目录";
        }

        if (UsesLegacyOption(req))
        {
            return "服务器任务不支持旧版兼容参数Aria2cProxy、OnlyHevc、OnlyAvc、OnlyAv1、AddDfnSubfix、NoPaddingPageNum、BandwithAscending，请改用对应的新参数";
        }

        var optionValidation = Program.ValidateWorkOptions(req);
        if (optionValidation is not null) return optionValidation;
        if (req.Interactive && !req.OnlyShowInfo)
            return "服务器任务不支持交互选择，请使用语言、AI策略及音视频筛选参数。";

        if (!serverOptions.AllowAria2cArgs && !string.IsNullOrWhiteSpace(req.Aria2cArgs))
        {
            return "服务器默认不允许传入Aria2cArgs，如确需使用请启动时配置 --server-allow-aria2c-args";
        }

        if (!serverOptions.AllowCustomNetworkHosts && HasCustomNetworkHost(req))
        {
            return "服务器默认不允许任务自定义解析或下载Host，如确需使用请启动时配置 --server-allow-custom-network-hosts";
        }

        if (!string.IsNullOrWhiteSpace(req.CallBackWebHook))
        {
            if (!Uri.TryCreate(req.CallBackWebHook, UriKind.Absolute, out var callbackUri)
                || (callbackUri.Scheme != Uri.UriSchemeHttp && callbackUri.Scheme != Uri.UriSchemeHttps))
            {
                return "CallBackWebHook必须是绝对HTTP或HTTPS URL";
            }

            if (!serverOptions.AllowPrivateCallbacks
                && IPAddress.TryParse(callbackUri.DnsSafeHost, out var callbackAddress)
                && TaskCallbackSender.IsPrivateOrReservedAddress(callbackAddress))
            {
                return "服务器默认不允许回调本机、内网或保留地址，如确需使用请启动时配置 --server-allow-private-callbacks";
            }
        }

        if (serverOptions.AllowCustomOutput)
        {
            if (string.IsNullOrWhiteSpace(req.WorkDir))
            {
                req.WorkDir = serverOptions.DownloadRoot;
                return null;
            }
            try
            {
                //Windows的ExpandEnvironmentVariables经Win32会在内嵌NUL处截断，必须在展开前拒绝，否则校验被静默绕过
                if (req.WorkDir.Contains('\0')) return "WorkDir不是有效路径";
                req.WorkDir = Path.GetFullPath(Environment.ExpandEnvironmentVariables(req.WorkDir), serverOptions.DownloadRoot);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return "WorkDir不是有效路径";
            }
            return null;
        }

        if (!string.IsNullOrWhiteSpace(req.WorkDir))
        {
            return "服务器默认不允许任务自定义WorkDir，如确需使用请启动时配置 --server-allow-custom-output";
        }

        if (HasUnsafeOutputPattern(req.FilePattern) || HasUnsafeOutputPattern(req.MultiFilePattern))
        {
            return "服务器默认只允许相对输出路径，且不能包含上级目录";
        }

        req.WorkDir = serverOptions.DownloadRoot;
        req.RestrictedOutputRoot = req.WorkDir;
        return null;
    }

    private static bool HasCustomNetworkHost(ServeRequestOptions req)
    {
        return !IsDefaultHost(req.Host, "api.bilibili.com")
            || !IsDefaultHost(req.EpHost, "api.bilibili.com")
            || !IsDefaultHost(req.TvHost, "api.snm0516.aisee.tv")
            || !string.IsNullOrWhiteSpace(req.UposHost)
            || req.AllowPcdn;
    }

    private static bool IsDefaultHost(string? actual, string expected)
    {
        return string.Equals(actual?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool UsesLegacyOption(MyOption option)
    {
        return !string.IsNullOrEmpty(option.Aria2cProxy) || option.OnlyHevc || option.OnlyAvc || option.OnlyAv1
            || option.AddDfnSubfix || option.NoPaddingPageNum || option.BandwithAscending;
    }

    private static bool HasUnsafeOutputPattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        if (Path.IsPathRooted(pattern))
        {
            return true;
        }

        return pattern
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(part => part == "..");
    }
}
