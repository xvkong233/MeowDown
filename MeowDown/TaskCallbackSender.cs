using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MeowDown.Core;

namespace MeowDown;

internal class TaskCallbackSender
{
    private readonly MeowDownServerOptions serverOptions;

    internal TaskCallbackSender(MeowDownServerOptions serverOptions)
    {
        this.serverOptions = serverOptions;
    }

    internal async Task SendCallbackAsync(ServeRequestOptions req, DownloadTask downloadTask)
    {
        if (string.IsNullOrEmpty(req.CallBackWebHook))
        {
            return;
        }

        try
        {
            var callbackUri = new Uri(req.CallBackWebHook, UriKind.Absolute);
            using var dnsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var addresses = await Dns.GetHostAddressesAsync(callbackUri.DnsSafeHost, dnsTimeout.Token);
            if (addresses.Length == 0)
            {
                Logger.LogDebug("回调已拒绝: 目标没有可用地址");
                return;
            }
            if (!serverOptions.AllowPrivateCallbacks && addresses.Any(IsPrivateOrReservedAddress))
            {
                Logger.LogDebug("回调已拒绝: 目标解析到本机、内网或保留地址");
                return;
            }

            string? jsonContent = JsonSerializer.Serialize(downloadTask.CreateSnapshot(), AppJsonSerializerContext.Default.DownloadTask);
            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            using var callbackHttpClient = CreatePinnedCallbackHttpClient(addresses[0]);
            using var response = await callbackHttpClient.PostAsync(callbackUri, content);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception e)
        {
            Logger.LogDebug("回调失败: {0}", e.Message);
        }
    }

    internal static HttpClient CreatePinnedCallbackHttpClient(IPAddress address)
    {
        return new HttpClient(CreatePinnedCallbackHandler(address), disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    internal static SocketsHttpHandler CreatePinnedCallbackHandler(IPAddress address)
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(
                        new IPEndPoint(address, context.DnsEndPoint.Port),
                        cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
    }

    internal static bool IsPrivateOrReservedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var ipv6Bytes = address.GetAddressBytes();
            return (ipv6Bytes[0] & 0xFE) == 0xFC
                || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || address.Equals(IPAddress.IPv6Any)
                || address.Equals(IPAddress.IPv6None);
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 0
            || bytes[0] == 10
            || bytes[0] == 127
            || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
            || (bytes[0] == 169 && bytes[1] == 254)
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || bytes[0] >= 224;
    }
}
