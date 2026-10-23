using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static MeowDown.Core.Logger;

namespace MeowDown;

internal sealed record DownloadResourceMetadata(long TotalLength, DownloadResumeValidator Validator);

internal sealed record DownloadResumeState(
    string ResourceIdentity, string SourceUriHash, long FromPosition, long? ToPosition, long TotalLength,
    bool Complete, long LocalLength, string LocalSha256, DownloadResumeValidator Validator)
{
    public int Version { get; init; } = 1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceObjectHash { get; init; }
    internal long RangeLength => (ToPosition ?? TotalLength - 1) - FromPosition + 1;

    internal static string Scope(string url, string? identity)
        => identity ?? SourceHash(url);

    internal static string SourceHash(string url)
    {
        var fragment = url.IndexOf('#');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fragment < 0 ? url : url[..fragment])));
    }

    internal bool MatchesSource(string url, string? sourceObjectHash)
        => SourceUriHash == SourceHash(url)
            || (!string.IsNullOrEmpty(SourceObjectHash) && !string.IsNullOrEmpty(sourceObjectHash)
                && SourceObjectHash == sourceObjectHash
                && Validator.IsUsable);

    internal bool MatchesRange(string identity, long from, long? to, long? total = null)
        => Version == 1 && ResourceIdentity == identity && FromPosition == from && ToPosition == to
            && (total is null || TotalLength == total) && LocalLength >= 0 && LocalLength <= RangeLength
            && (!Complete || LocalLength == RangeLength);

    // Shared restore gate for the whole-file and per-clip trackers: the saved
    // record must still describe this exact range and resource version, and the
    // bytes on disk must hash to the recorded prefix. Its reuse terms mirror
    // DeriveResumePlan item by item — same source, usable validator that still
    // matches the expected resource, KnownChanged as the gate — so a state
    // restored without verification is exactly one the download plan would
    // append to without re-reading the retained bytes.
    internal static async Task<RestoreDecision?> EvaluateRestoreAsync(
        DownloadResumeState? saved, string identity, long from, long? to,
        string url, string? sourceObjectHash, DownloadResourceMetadata? metadata, string filePath)
    {
        if (saved is not { } state
            || !state.MatchesRange(identity, from, to, metadata?.TotalLength)
            || (metadata is { } expected && state.Validator.KnownChanged(expected.Validator))
            || !await state.MatchesFileAsync(filePath))
            return null;
        var sameSource = state.MatchesSource(url, sourceObjectHash);
        var sameVersion = state.Validator.IsUsable
            && (metadata is null || state.Validator.Matches(metadata.Validator));
        var needsVerification = !sameSource || !sameVersion
            || new FileInfo(filePath).Length > state.LocalLength;
        return new RestoreDecision(state.LocalLength, needsVerification, sameSource, sameVersion);
    }

    internal async Task<bool> MatchesFileAsync(string path)
    {
        if (!File.Exists(path)) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await MatchesStreamAsync(stream);
    }

    internal async Task<bool> MatchesStreamAsync(Stream stream)
    {
        if (stream.Length < LocalLength || (Complete && stream.Length != LocalLength) || LocalSha256.Length != 64) return false;
        stream.Position = 0;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[262144];
        var remaining = LocalLength;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)));
            if (read == 0) return false;
            digest.AppendData(buffer, 0, read);
            remaining -= read;
        }
        var hash = Convert.ToHexString(digest.GetHashAndReset());
        stream.Position = stream.Length;
        return hash.Equals(LocalSha256, StringComparison.Ordinal);
    }

    internal static DownloadResumeState? Parse(string text, string? sourcePath = null)
    {
        try
        {
            var state = JsonSerializer.Deserialize(text, DownloadResumeStateJsonContext.Default.DownloadResumeState);
            return state is { Version: 1, ResourceIdentity: not null, SourceUriHash: not null, LocalSha256: not null, Validator: not null }
                && state.FromPosition >= 0 && state.TotalLength > state.FromPosition
                && state.RangeLength > 0 && state.RangeLength <= state.TotalLength - state.FromPosition
                && state.LocalLength >= 0 && state.LocalLength <= state.RangeLength
                && (!state.Complete || state.LocalLength == state.RangeLength) ? state : null;
        }
        catch (JsonException ex)
        {
            LogWarn(sourcePath is null
                ? $"解析续传状态记录失败: {ex.Message}，已忽略该记录"
                : $"解析续传状态文件 {sourcePath} 失败: {ex.Message}，已忽略该记录");
            return null;
        }
    }

    internal static async Task<DownloadResumeState?> LoadAsync(string path)
        => File.Exists(path) ? Parse(await File.ReadAllTextAsync(path), path) : null;

    internal async Task SaveAsync(string path, string? restrictedOutputRoot = null)
    {
        path = OutputPathPolicy.ResolveArtifact(path, restrictedOutputRoot);
        var temporary = OutputPathPolicy.ResolveArtifact(Path.Combine(Path.GetDirectoryName(path) ?? "",
            ".bbdownt-" + Guid.NewGuid().ToString("N") + ".resume.writing"), restrictedOutputRoot);
        try
        {
            await File.WriteAllTextAsync(temporary,
                JsonSerializer.Serialize(this, DownloadResumeStateJsonContext.Default.DownloadResumeState));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

[JsonSerializable(typeof(DownloadResumeState))]
internal partial class DownloadResumeStateJsonContext : JsonSerializerContext { }

// Verdict of the shared restore gate: whether the saved resume record may be
// restored into the progress tracker, plus the facts that drove the decision
// so callers can keep logging them unchanged.
internal readonly record struct RestoreDecision(
    long LocalLength, bool NeedsVerification, bool SameSource, bool SameVersion);
