using System;
using System.Net;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace BBDownT;

internal class ServerAuth
{
    internal string apiToken = "";
    internal bool requireApiToken;

    internal void ConfigureApiToken(Uri listenUri, string? configuredApiToken)
    {
        var token = configuredApiToken?.Trim() ?? "";
        var generatedToken = false;
        if (string.IsNullOrEmpty(token) && !IsLoopbackListenHost(listenUri.Host))
        {
            token = GenerateApiToken();
            generatedToken = true;
        }

        apiToken = token;
        requireApiToken = !string.IsNullOrEmpty(apiToken);

        if (!requireApiToken)
        {
            Console.WriteLine($"API鉴权未启用：{TokenlessAccessMessage}。");
            return;
        }

        Console.WriteLine("API鉴权已启用，请在请求头中携带 Authorization: Bearer <token> 或 X-BBDownT-Token: <token>。");
        if (generatedToken)
        {
            Console.WriteLine($"本次自动生成的API Token: {apiToken}");
        }
    }

    private static bool IsLoopbackListenHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }

    internal const string TokenlessAccessMessage =
        "只接受来自本机的直接请求，如需经反向代理、其他主机名或外部网页访问，请配置 --api-token";

    private static readonly string[] ForwardingHeaders = ["Forwarded", "X-Real-IP", "Via"];

    internal static bool IsDirectLocalRequest(HttpRequest request)
    {
        if (!IsLoopbackListenHost(request.Host.Host.Trim('[', ']'))) return false;
        if (request.Headers.TryGetValue("Origin", out var origin) && !IsLoopbackOrigin(origin.ToString()))
            return false;
        return !request.Headers.Keys.Any(header => ForwardingHeaders.Contains(header, StringComparer.OrdinalIgnoreCase)
            || header.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsLoopbackOrigin(string origin)
    {
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && IsLoopbackListenHost(uri.Host.Trim('[', ']'));
    }

    private static string GenerateApiToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }

    internal bool HasValidApiToken(HttpContext context)
    {
        if (!requireApiToken)
        {
            return true;
        }

        if (context.Request.Headers.TryGetValue("Authorization", out var authorization))
        {
            const string bearerPrefix = "Bearer ";
            var authorizationValue = authorization.ToString();
            if (authorizationValue.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
                && IsApiTokenMatch(authorizationValue[bearerPrefix.Length..].Trim()))
            {
                return true;
            }
        }

        return context.Request.Headers.TryGetValue("X-BBDownT-Token", out var token)
            && IsApiTokenMatch(token.ToString().Trim());
    }

    private bool IsApiTokenMatch(string candidate)
    {
        if (string.IsNullOrEmpty(candidate) || candidate.Length != apiToken.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidate),
            Encoding.UTF8.GetBytes(apiToken));
    }
}
