using MeowDown.Core.Entity;
using static MeowDown.Core.Util.HTTPUtil;

namespace MeowDown.Core.Fetcher;

/// <summary>
/// 合集解析
/// https://space.bilibili.com/23630128/channel/collectiondetail?sid=2045
/// </summary>
public class MediaListFetcher : IFetcher
{
    public async Task<VInfo> FetchAsync(string id)
    {
        var bizId = id[10..];
        return await MediaListFetcherCore.FetchAsync(
            bizId,
            type: 8,
            descending: false,
            includeBvid: false,
            displayName: "合集",
            url => GetWebSourceAsync(url),
            () => new SeriesListFetcher().FetchAsync($"seriesBizId:{bizId}"));
    }
}
