using MusicTagClone.Interfaces;
using MusicTagClone.Models;
using MusicTagClone.Services;

namespace MusicTagClone.Tests.Services;

/// <summary>
/// 网易云音乐 — 封面搜索/下载 API 综合测试
///
/// 测试端点（无需加密）：
///   搜索: POST https://music.163.com/api/cloudsearch/pc
///         body: s=&lt;query&gt;&amp;type=1&amp;limit=&lt;n&gt;&amp;offset=0
///         → result.songs[].al.picUrl
///   详情: POST http://music.163.com/api/song/detail/?id=&lt;id&gt;&amp;ids=[&lt;id&gt;] （备选）
///
/// 注意事项：
///   - 网络依赖测试，要求机器有互联网连接
///   - 网易云 API 可能有频率限制，批量测试时注意间隔
/// </summary>
[Trait("Category", "Network")] // 联网测试，CI 跳过
public class CoverServiceNeteaseTests
{
    private readonly CoverService _service;

    /// <summary>覆盖各语种的测试歌曲</summary>
    public record NeteaseSong(string Artist, string Title, string Album)
    {
        public MusicFile ToMusicFile() => new() { Artist = Artist, Title = Title, Album = Album };
    }

    public static readonly NeteaseSong[] Songs =
    {
        // 中文经典
        new("周杰伦", "晴天", "叶惠美"),
        new("周杰伦", "七里香", "七里香"),
        new("林俊杰", "江南", "第二天堂"),
        new("邓紫棋", "光年之外", "光年之外"),
        new("陈奕迅", "十年", "黑白灰"),
        new("Beyond", "海阔天空", "乐与怒"),
        new("王菲", "红豆", "唱游"),
        // 英文/日文
        new("Taylor Swift", "Love Story", "Fearless"),
        new("YOASOBI", "夜に駆ける", "THE BOOK"),
    };

    public CoverServiceNeteaseTests()
    {
        var mockSettings = new Moq.Mock<ISettingsService>();
        mockSettings.Setup(s => s.ItunesSearchParamsCountry).Returns("CN");
        var httpClientFactoryMock = new Moq.Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient("default")).Returns(new HttpClient());
        var mockLogger = new Moq.Mock<ILoggerService>();
        _service = new CoverService(httpClientFactoryMock.Object, mockSettings.Object, mockLogger.Object, new FakeImageCache());
    }

    public static IEnumerable<object[]> GetSongs()
        => Songs.Select(s => new object[] { s });

    #region 基本搜索

    /// <summary>每首歌搜索都应返回非空封面 URL</summary>
    [Theory]
    [MemberData(nameof(GetSongs))]
    public async Task Search_应返回有效封面(NeteaseSong song)
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
                file, "netease", condition, cts.Token);
        }
        catch (HttpRequestException ex)
        {
            Assert.Fail($"API请求失败 (接口无效或网络不可达): {ex.Message}");
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
            Assert.Equal("网易云音乐", r.SourceName);
            Assert.False(string.IsNullOrEmpty(r.CoverUrl),
                $"结果 [{r.Title}] 缺少 CoverUrl");
            Assert.True(r.CoverUrl!.StartsWith("http"),
                $"CoverUrl 格式不正确: {r.CoverUrl}");
            Assert.False(string.IsNullOrEmpty(r.Title),
                "搜索结果的 Title 不应为空");
        });
    }

    /// <summary>验证 CoverUrl 是完整可用的 HTTPS 图片链接</summary>
    [Theory]
    [MemberData(nameof(GetSongs))]
    public async Task CoverUrl格式验证(NeteaseSong song)
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
                file, "netease", condition, cts.Token);
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

        // 验证所有 CoverUrl 符合网易云图片链接格式
        Assert.All(results, r =>
        {
            var url = r.CoverUrl!;
            // 网易云封面格式: https://p*.music.126.net/...
            Assert.True(url.Contains("music.126.net") || url.Contains("music.163.com"),
                $"CoverUrl 域名不符合预期: {url}");
            Assert.True(url.StartsWith("https://"),
                $"CoverUrl 应使用 HTTPS: {url}");
            Assert.True(url.Length > 30,
                $"CoverUrl 过短，可能不是完整图片链接: {url}");
        });
    }

    #endregion

    #region 搜索 + 下载串联

    /// <summary>搜索 → 下载封面图 → 验证图片尺寸和格式</summary>
    [Theory]
    [MemberData(nameof(GetSongs))]
    public async Task SearchAndDownload_封面可成功下载(NeteaseSong song)
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
            MaxSizeKB = 10240, // 宽松限制
        };

        // 1. 搜索
        IReadOnlyList<SearchResult> results;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            results = await _service.SearchCoversFromSourceAsync(
                file, "netease", condition, cts.Token);
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

        // 2. 下载前3个封面 URL
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
                    Assert.True(cover.Width <= 6000, $"图片宽度过大 ({cover.Width}px): {result.CoverUrl}");
                    Assert.True(cover.Height <= 6000, $"图片高度过大 ({cover.Height}px): {result.CoverUrl}");
                    Assert.True(cover.FileSizeBytes >= 1000, $"图片文件过小 ({cover.FileSizeBytes}B): {result.CoverUrl}");
                    Assert.True(cover.MimeType == "image/jpeg" || cover.MimeType == "image/png",
                        $"MIME 类型异常: {cover.MimeType}");
                    downloaded++;
                    break; // 一个封面下载成功即可
                }
            }
            catch (HttpRequestException)
            {
                continue; // 尝试下一个结果
            }
            catch (TaskCanceledException)
            {
                continue;
            }
        }

        Assert.True(downloaded >= 1,
            $"所有搜索结果封面下载均失败: {song.Artist} - {song.Title}");
    }

    #endregion

    #region 边缘场景

    /// <summary>空 Artist 搜索应优雅处理</summary>
    [Fact]
    public async Task Search_空Artist_不抛异常()
    {
        var file = new MusicFile { Artist = "", Title = "晴天" };
        var condition = new SearchCondition { UseArtist = false, WebSearchItemsLimit = 3 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var results = await _service.SearchCoversFromSourceAsync(
            file, "netease", condition, cts.Token);

        Assert.NotNull(results);
    }

    /// <summary>空 Title 搜索应优雅处理</summary>
    [Fact]
    public async Task Search_空Title_不抛异常()
    {
        var file = new MusicFile { Artist = "周杰伦", Title = "" };
        var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 3 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var results = await _service.SearchCoversFromSourceAsync(
            file, "netease", condition, cts.Token);

        Assert.NotNull(results);
    }

    /// <summary>查询含特殊字符不抛异常</summary>
    [Fact]
    public async Task Search_特殊字符_不抛异常()
    {
        var file = new MusicFile { Artist = "テスト", Title = "曲" };
        var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 3 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var results = await _service.SearchCoversFromSourceAsync(
            file, "netease", condition, cts.Token);

        Assert.NotNull(results); // 允许返回空结果，但不抛异常
    }

    /// <summary>不存在的歌名返回空列表而不是抛异常</summary>
    [Fact]
    public async Task Search_不存在的歌曲_返回空列表()
    {
        var file = new MusicFile
        {
            Artist = "ThisArtistDoesNotExist12345XYZ",
            Title = "ThisSongDoesNotExist67890ABC"
        };
        var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 3 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var results = await _service.SearchCoversFromSourceAsync(
            file, "netease", condition, cts.Token);

        // 不存在时可能返回空列表，也可能因模糊匹配返回少量结果
        // 重点是搜索过程不抛异常
        Assert.NotNull(results);
    }

    /// <summary>
    /// 验证网易云返回的 CoverUrl 的域名是否可直连下载。
    /// 网易云封面 CDN 曾经从 *.music.126.net 下载失败（403/SSL错误），
    /// 所以需要确认当前的 CDN 域名可直连。
    /// </summary>
    [Fact]
    public async Task NeteaseCdnDirect_已知封面URL_应可下载()
    {
        // 从已知歌曲的搜索结果中提取封面 URL 并测试 CDN 直连
        var file = new MusicFile { Artist = "周杰伦", Title = "晴天" };
        var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 3 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var results = await _service.SearchCoversFromSourceAsync(
            file, "netease", condition, cts.Token);

        if (results.Count == 0)
            return; // 无结果时跳过

        var coverUrl = results[0].CoverUrl!;
        Assert.NotEmpty(coverUrl);

        // 直连下载验证
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var request = new HttpRequestMessage(HttpMethod.Get, coverUrl);
        request.Headers.Add("Referer", "https://music.163.com");
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.3; Win64; x64) AppleWebKit/537.36");

        var response = await http.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode,
            $"HTTP {(int)response.StatusCode} — 封面 CDN 返回错误: {coverUrl}");

        var data = await response.Content.ReadAsByteArrayAsync();
        Assert.True(data.Length >= 1000,
            $"图片文件过小 ({data.Length}B): {coverUrl}");
        Assert.True(data.Length <= 5 * 1024 * 1024,
            $"图片文件过大 ({data.Length / 1024}KB): {coverUrl}");
    }

    #endregion
}
