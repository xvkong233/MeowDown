using System.Text.Json.Nodes;
using BBDownT.Core.Entity;
using BBDownT.Core.Fetcher;
using static BBDownT.Core.Entity.Entity;

namespace BBDownT.Tests;

public class NormalInfoFetcherSeamTests
{
    [Fact]
    public async Task BangumiRedirect_SinglePageBackfillsEpIdThroughSeam()
    {
        var calls = new List<string>();

        var result = await NormalInfoFetcher.FetchAsync(
            "2",
            url =>
            {
                calls.Add(url);
                return Task.FromResult(
                    ViewFixture(
                        isSteinGate: 0,
                        redirectUrl: "https://www.bilibili.com/bangumi/play/ep777",
                        Page(1, 201, "正片")));
            },
            _ => throw new InvalidOperationException("player.so 不应在非互动视频上请求"),
            _ => throw new InvalidOperationException("edgeinfo 不应在非互动视频上请求"));

        Assert.Equal(new[] { "https://api.bilibili.com/x/web-interface/view?aid=2" }, calls);
        Assert.True(result.IsBangumi);
        var page = Assert.Single(result.PagesInfo);
        Assert.Equal("777", page.epid);
        Assert.Equal("201", page.cid);
    }

    [Fact]
    public async Task BangumiRedirect_MultiPageKeepsEpIdEmpty()
    {
        var result = await NormalInfoFetcher.FetchAsync(
            "2",
            _ => Task.FromResult(ViewFixture(
                isSteinGate: 0,
                redirectUrl: "https://www.bilibili.com/bangumi/play/ep777",
                Page(1, 201, "P1"),
                Page(2, 202, "P2"))),
            _ => throw new InvalidOperationException("player.so 不应在非互动视频上请求"),
            _ => throw new InvalidOperationException("edgeinfo 不应在非互动视频上请求"));

        Assert.True(result.IsBangumi);
        Assert.Equal(2, result.PagesInfo.Count);
        Assert.All(result.PagesInfo, page => Assert.Equal("", page.epid));
    }

    [Fact]
    public async Task SteinGate_AppendsChoicePagesThroughPlayerSoAndEdgeInfoSeam()
    {
        var calls = new List<string>();

        var result = await NormalInfoFetcher.FetchAsync(
            "2",
            url =>
            {
                calls.Add(url);
                return Task.FromResult(ViewFixture(
                    isSteinGate: 1,
                    redirectUrl: null,
                    Page(1, 201, "正片")));
            },
            url =>
            {
                calls.Add(url);
                return Task.FromResult("""<interaction>{"graph_version": 123456}</interaction>""");
            },
            url =>
            {
                calls.Add(url);
                return Task.FromResult(EdgeInfoFixture());
            });

        Assert.Equal(new[]
        {
            "https://api.bilibili.com/x/web-interface/view?aid=2",
            "https://api.bilibili.com/x/player.so?bvid=BV1xx411c7mD&id=cid:201",
            "https://api.bilibili.com/x/stein/edgeinfo_v2?graph_version=123456&bvid=BV1xx411c7mD"
        }, calls);
        Assert.True(result.IsSteinGate);
        Assert.False(result.IsBangumi);
        Assert.Equal(3, result.PagesInfo.Count);
        Assert.Equal(new[] { 1, 2, 3 }, result.PagesInfo.Select(page => page.index));
        var first = result.PagesInfo[0];
        Assert.Equal("201", first.cid);
        Assert.Equal("正片", first.title);
        Assert.Equal("", first.epid);
        Assert.Equal("选择A", result.PagesInfo[1].title);
        Assert.Equal("301", result.PagesInfo[1].cid);
        Assert.Equal(0, result.PagesInfo[1].dur);
        Assert.Equal("选择B", result.PagesInfo[2].title);
        Assert.Equal("302", result.PagesInfo[2].cid);
    }

    private static string ViewFixture(int isSteinGate, string? redirectUrl, params JsonNode[] pages)
    {
        var data = new JsonObject
        {
            ["bvid"] = "BV1xx411c7mD",
            ["title"] = " Title ",
            ["desc"] = " Desc ",
            ["pic"] = "cover.jpg",
            ["cid"] = 201,
            ["pubdate"] = 100,
            ["rights"] = new JsonObject { ["is_stein_gate"] = isSteinGate },
            ["owner"] = new JsonObject { ["mid"] = 7, ["name"] = "Owner" },
            ["pages"] = new JsonArray(pages)
        };
        if (redirectUrl is not null) data["redirect_url"] = redirectUrl;
        return new JsonObject { ["code"] = 0, ["data"] = data }.ToJsonString();
    }

    private static JsonObject Page(int page, int cid, string part) => new()
    {
        ["page"] = page,
        ["cid"] = cid,
        ["part"] = part,
        ["duration"] = 60
    };

    private static string EdgeInfoFixture() => new JsonObject
    {
        ["code"] = 0,
        ["data"] = new JsonObject
        {
            ["edges"] = new JsonObject
            {
                ["questions"] = new JsonArray(
                    new JsonObject
                    {
                        ["choices"] = new JsonArray(
                            new JsonObject { ["cid"] = 301, ["option"] = " 选择A " },
                            new JsonObject { ["cid"] = 302, ["option"] = "选择B" })
                    })
            }
        }
    }.ToJsonString();
}
