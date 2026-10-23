using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text.Json;
using MeowDown.Core.Entity;
using MeowDown.Core.Util;

namespace MeowDown;

// A page retry refreshes metadata and media addresses. Transport retries and
// completed-part validation remain owned by the HTTP client and downloader.
internal sealed class PageDownloadRetry
{
    private static readonly TimeSpan[] NetworkDelays =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)];
    private int networkRetries;
    private int otherRetries;

    internal bool TryGetDelay(Exception error, out TimeSpan delay)
    {
        delay = default;
        var traits = Classify(error);
        if (traits.IsSubtitleUnavailable) return false;
        if (traits.IsNetwork)
        {
            if (networkRetries >= NetworkDelays.Length) return false;
            delay = NetworkDelays[networkRetries++];
            return true;
        }

        // Invalid options, malformed data, unsupported transports, explicit
        // cancellation and local storage failures cannot be cured by waiting.
        if (traits.IsFatal) return false;

        // Preserve the legacy short reparse budget for 403/404. These statuses
        // alone cannot distinguish an unusable media URL from denied access;
        // the transport layer must still fail without retrying that same URL.
        if (otherRetries >= 2) return false;
        otherRetries++;
        delay = TimeSpan.FromSeconds(3);
        return true;
    }

    internal static bool IsNetworkFailure(Exception error) => Classify(error).IsNetwork;

    // One walk over the cause chain yields every fact the page-retry policy and
    // the batch stop decision need, so their verdicts cannot drift apart. The
    // reparse budget is precomputed over the whole chain before it participates
    // in the fatal determination: a 403/404 HttpRequestException anywhere in the
    // chain exempts every IOException/HttpRequestException of that chain.
    internal static ErrorTraits Classify(Exception error)
    {
        var subtitleUnavailable = false;
        var reparseHttpFailure = false;
        var cancellation = false;
        var fatalException = false;
        var fatalType = false;
        var ioOrHttp = false;
        BilibiliApiException? api = null;
        for (Exception? cause = error; cause is not null; cause = cause.InnerException)
        {
            subtitleUnavailable |= cause is SubtitleUnavailableException;
            reparseHttpFailure |= cause is HttpRequestException
                { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound };
            fatalType |= cause is AudioLanguageUnavailableException or IntlApiException or BilibiliApiException
                or ArgumentException or NotSupportedException or UnauthorizedAccessException
                or InvalidDataException or JsonException or AuthenticationException or OperationCanceledException
                or OutOfMemoryException or AccessViolationException;
            cancellation |= cause is OperationCanceledException;
            fatalException |= cause is OutOfMemoryException or AccessViolationException;
            ioOrHttp |= cause is IOException or HttpRequestException;
            api ??= cause as BilibiliApiException;
        }
        return new ErrorTraits(
            subtitleUnavailable,
            fatalType || (ioOrHttp && !reparseHttpFailure),
            NetworkRetry.IsTransient(error) || (api is { } first && BilibiliApi.IsRateLimited(first.Code)),
            reparseHttpFailure,
            cancellation,
            fatalException);
    }
}

// Classification of one error's cause chain, shared by PageDownloadRetry and
// PageDownloadRunner. IsFatal is the reparse policy's "cannot be cured by
// waiting" verdict; the raw cancellation and fatal-exception facts stay
// separate because the batch stop decision applies its own rule: an
// OperationCanceledException only stops the batch when the whole error is not
// transient (see StopsBatch).
internal sealed record ErrorTraits(
    bool IsSubtitleUnavailable,
    bool IsFatal,
    bool IsNetwork,
    bool IsReparseHttpFailure,
    bool HasCancellation,
    bool HasFatalException)
{
    internal bool StopsBatch(bool transient) =>
        IsSubtitleUnavailable || HasFatalException || (HasCancellation && !transient);
}
