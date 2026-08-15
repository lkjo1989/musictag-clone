using System.Net;
using MusicTagClone.Interfaces;
using MusicTagClone.Models;
using MusicTagClone.Services;

namespace MusicTagClone.Tests.Services;

/// <summary>
/// Last.fm 封面搜索 API 综合测试
///
/// 端点：
///   album.search: GET https://ws.audioscrobbler.com/2.0/
///                 ?method=album.search&album={query}&api_key={key}&format=json&limit={n}
///                 → results.albummatches.album[].image[{size, #text}]
///
/// 已知限制：
///   - Last.fm 专辑封面 CDN 域名: lastfm.freetls.fastly.net
///   - 无封面时返回包含 hash 2a96cbd8b46e8629b05392ea4b2c726b 的占位图 URL
///   - album.search 搜索的是专辑名，不是歌曲名
///   - 使用默认代理 http://127.0.0.1:7890 访问国外网站
/// </summary>
public class CoverServiceLastfmTests
{
    private readonly CoverService _service;
    private const string DefaultProxyUrl = "http://127.0.0.1:7890";

    public record LastfmSong(string Artist, string Title, string Album)
    {
        public MusicFile ToMusicFile() => new() { Artist = Artist, Title = Title, Album = Album };
    }

    /// <summary>测试专辑列表 — 覆盖各语种、各年代</summary>
    public static readonly LastfmSong[] Albums =
    {
        // 中文经典
        new("周杰伦", "晴天", "叶惠美"),
        new("周杰伦", "七里香", "七里香"),
        new("林俊杰", "江南", "第二天堂"),
        new("邓紫棋", "光年之外", "光年之外"),
        new("陈奕迅", "十年", "黑白灰"),
        new("Beyond", "海阔天空", "乐与怒"),
        new("王菲", "红豆", "唱游"),
        // 英文流行
        new("Taylor Swift", "Shake It Off", "1989"),
        new("Ed Sheeran", "Shape of You", "÷ (Divide)"),
        new("Adele", "Hello", "25"),
        // 日文
        new("YOASOBI", "夜に駆ける", "THE BOOK"),
    };

    public CoverServiceLastfmTests()
    {
        var mockSettings = new Moq.Mock<ISettingsService>();
        mockSettings.Setup(s => s.ItunesSearchParamsCountry).Returns("CN");
        mockSettings.Setup(s => s.ProxyUrl).Returns(DefaultProxyUrl);

        var handler = new HttpClientHandler
        {
            Proxy = new WebProxy(DefaultProxyUrl),
            UseProxy = true,
        };
        var httpClientFactoryMock = new Moq.Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient("default")).Returns(new HttpClient(handler));
        var mockLogger = new Moq.Mock<ILoggerService>();
        _service = new CoverService(httpClientFactoryMock.Object, mockSettings.Object, mockLogger.Object, new FakeImageCache());
    }

    public static IEnumerable<object[]> GetAlbums()
        => Albums.Select(a => new object[] { a });

    #region 基本搜索

    /// <summary>每张专辑搜索都应返回非空封面 URL</summary>
    [Theory]
    [MemberData(nameof(GetAlbums))]
    public async Task Search_应返回有效封面(LastfmSong song)
    {
        var file = song.ToMusicFile();
        var condition = new SearchCondition
        {
            UseArtist = true,
            UseAlbum = true,
            WebSearchItemsLimit = 5,
        };

        IReadOnlyList<SearchResult> results;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            results = await _service.SearchCoversFromSourceAsync(
                file, "lastfm", condition, cts.Token);
        }
        catch (HttpRequestException ex)
        {
            Assert.Fail($"API请求失败 (Last.fm不可达): {ex.Message}");
            return;
        }
        catch (TaskCanceledException)
        {
            Assert.Fail("API请求超时");
            return;
        }

        Assert.NotEmpty(results);
        Assert.All(results, r =>
        {
            Assert.Equal("Last.fm", r.SourceName);
            Assert.False(string.IsNullOrEmpty(r.CoverUrl),
                $"结果 [{r.Title}] 缺少 CoverUrl");
            Assert.StartsWith("http", r.CoverUrl!);
            Assert.False(string.IsNullOrEmpty(r.Title),
                "搜索结果的 Title 不应为空");
        });
    }

    /// <summary>验证 CoverUrl 是 Last.fm CDN 图片链接</summary>
    [Theory]
    [MemberData(nameof(GetAlbums))]
    public async Task CoverUrl格式验证(LastfmSong song)
    {
        var file = song.ToMusicFile();
        var condition = new SearchCondition
        {
            UseArtist = true,
            UseAlbum = true,
            WebSearchItemsLimit = 5,
        };

        IReadOnlyList<SearchResult> results;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            results = await _service.SearchCoversFromSourceAsync(
                file, "lastfm", condition, cts.Token);
        }
        catch (HttpRequestException ex)
        {
            Assert.Fail($"API请求失败: {ex.Message}");
            return;
        }
        catch (TaskCanceledException)
        {
            Assert.Fail("API请求超时");
            return;
        }

        if (results.Count == 0)
        {
            Assert.Fail($"无搜索结果: {song.Artist} - {song.Title}");
            return;
        }

        Assert.All(results, r =>
        {
            var url = r.CoverUrl!;
            // Last.fm CDN: lastfm.freetls.fastly.net 或 lastfm-img2.akamaized.net
            Assert.True(
                url.Contains("lastfm") || url.Contains("last.fm") || url.Contains("akamaized"),
                $"CoverUrl 域名不符合预期: {url}");
            Assert.False(url.Contains("2a96cbd8b46e8629b05392ea4b2c726b"),
                $"CoverUrl 是无封面占位图: {url}");
        });
    }

    #endregion

    #region 搜索 + 下载串联

    /// <summary>搜索 → 下载封面图 → 验证图片尺寸和格式</summary>
    /// <remarks>
    /// Last.fm CDN (lastfm.freetls.fastly.net) 在部分网络环境下可能不可达，
    /// 下载失败时记录而非断言失败。
    /// </remarks>
    [Theory]
    [MemberData(nameof(GetAlbums))]
    public async Task SearchAndDownload_封面可成功下载(LastfmSong song)
    {
        var file = song.ToMusicFile();
        var condition = new SearchCondition
        {
            UseArtist = true,
            UseAlbum = true,
            WebSearchItemsLimit = 5,
        };
        var limits = new CoverArt.LimitsConfig
        {
            MaxResolution = 5000,
            MaxSizeKB = 10240,
        };

        IReadOnlyList<SearchResult> results;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            results = await _service.SearchCoversFromSourceAsync(
                file, "lastfm", condition, cts.Token);
        }
        catch (HttpRequestException ex)
        {
            Assert.Fail($"搜索失败: {ex.Message}");
            return;
        }
        catch (TaskCanceledException)
        {
            Assert.Fail("搜索超时");
            return;
        }

        if (results.Count == 0)
        {
            Assert.Fail($"无搜索结果: {song.Artist} - {song.Title}");
            return;
        }

        var valid = results.Where(r => !string.IsNullOrEmpty(r.CoverUrl)).ToList();
        Assert.NotEmpty(valid);

        // 验证 CoverUrl 格式正确（不包含占位图 hash）
        Assert.All(valid, r =>
        {
            Assert.False(r.CoverUrl!.Contains("2a96cbd8b46e8629b05392ea4b2c726b"),
                $"CoverUrl 是无封面占位图: {r.CoverUrl}");
        });

        // 尝试下载封面（CDN 可能不可达，不作为断言失败条件）
        var downloaded = 0;
        foreach (var result in valid.Take(3))
        {
            using var dcts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                var cover = await _service.DownloadCoverAsync(result, limits, dcts.Token);
                if (cover != null && cover.HasImage)
                {
                    Assert.True(cover.Width >= 50, $"图片宽度异常 ({cover.Width}px): {result.CoverUrl}");
                    Assert.True(cover.Height >= 50, $"图片高度异常 ({cover.Height}px): {result.CoverUrl}");
                    downloaded++;
                    break;
                }
            }
            catch (HttpRequestException)
            {
                continue;
            }
            catch (TaskCanceledException)
            {
                continue;
            }
        }

        if (downloaded == 0)
        {
            // Last.fm CDN (fastly.net) 在部分网络环境可能不可达
            Assert.Fail($"封面下载失败（可能 Last.fm CDN 不可达）: {song.Artist} - {song.Title}");
        }
    }

    #endregion

    #region 边缘场景

    /// <summary>空查询不抛异常</summary>
    [Fact]
    public async Task Search_空查询_不抛异常()
    {
        var file = new MusicFile { Artist = "", Title = "" };
        var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 3 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var results = await _service.SearchCoversFromSourceAsync(
            file, "lastfm", condition, cts.Token);

        Assert.NotNull(results);
    }

    /// <summary>不存在的专辑返回空列表或不抛异常</summary>
    [Fact]
    public async Task Search_不存在的专辑_不抛异常()
    {
        var file = new MusicFile
        {
            Artist = "ThisArtistDoesNotExistXYZ",
            Album = "ThisAlbumDoesNotExistXYZ"
        };
        var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 3 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var results = await _service.SearchCoversFromSourceAsync(
            file, "lastfm", condition, cts.Token);

        Assert.NotNull(results);
    }

    /// <summary>特殊字符查询不抛异常</summary>
    [Fact]
    public async Task Search_特殊字符_不抛异常()
    {
        var file = new MusicFile { Artist = "テスト", Album = "曲" };
        var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 3 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var results = await _service.SearchCoversFromSourceAsync(
            file, "lastfm", condition, cts.Token);

        Assert.NotNull(results);
    }

    /// <summary>验证 Last.fm API 返回的图片不是占位图</summary>
    [Fact]
    public async Task Search_热门专辑_应返回非占位图封面()
    {
        var file = new MusicFile { Artist = "Taylor Swift", Title = "Shake It Off", Album = "1989" };
        var condition = new SearchCondition { UseArtist = true, UseAlbum = true, WebSearchItemsLimit = 5 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var results = await _service.SearchCoversFromSourceAsync(
            file, "lastfm", condition, cts.Token);

        Assert.NotEmpty(results);
        // 热门专辑应该至少有一个结果有真实封面（不是占位图）
        var validCover = results.FirstOrDefault(r =>
            !string.IsNullOrEmpty(r.CoverUrl) &&
            !r.CoverUrl!.Contains("2a96cbd8b46e8629b05392ea4b2c726b"));
        Assert.NotNull(validCover);
    }

    #endregion
}
