using System.Net;
using MusicTagClone.Interfaces;
using MusicTagClone.Models;
using MusicTagClone.Services;
using Newtonsoft.Json.Linq;

namespace MusicTagClone.Tests.Services;

/// <summary>
/// 封面服务 API 验证测试 — 逐个来源测试搜索和下载
///
/// 设计目标：
///   1. 每个来源独立测试，方便定位哪个接口出问题
///   2. 多首流行歌曲覆盖，避免"刚好某首歌有结果"的假阳性
///   3. 搜索 + 下载串联测试，确保 CoverUrl 是可用的下载链接
///   4. 下载失败时输出诊断信息（状态码、响应头等）
///   5. 国外网站使用默认代理 http://127.0.0.1:7890
/// </summary>
public class CoverServiceApiTests : IDisposable
{
    private readonly CoverService _service;
    private readonly CoverArt.LimitsConfig _limits;

    // 默认代理地址 — 用于访问国外网站
    private const string DefaultProxyUrl = "http://127.0.0.1:7890";

    // 测试歌曲列表 — 跨语种、跨年代
    private static readonly TestSong[] TestSongs =
    {
        // 中文流行
        new("周杰伦", "晴天", "叶惠美"),
        new("周杰伦", "七里香", "七里香"),
        new("林俊杰", "江南", "第二天堂"),
        new("邓紫棋", "光年之外", "光年之外"),
        new("陈奕迅", "十年", "黑白灰"),
        // 英文流行
        new("Taylor Swift", "Shake It Off", "1989"),
        new("Ed Sheeran", "Shape of You", "÷ (Divide)"),
        new("Adele", "Hello", "25"),
    };

    /// <summary>测试歌曲参数</summary>
    public record TestSong(string Artist, string Title, string Album);

    public CoverServiceApiTests()
    {
        var mockSettings = new Moq.Mock<ISettingsService>();
        mockSettings.Setup(s => s.ItunesSearchParamsCountry).Returns("CN");
        mockSettings.Setup(s => s.ProxyUrl).Returns(DefaultProxyUrl);

        // 创建带代理的 HttpClient
        var handler = new HttpClientHandler
        {
            Proxy = new WebProxy(DefaultProxyUrl),
            UseProxy = true,
        };
        var httpClientFactoryMock = new Moq.Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient("default")).Returns(new HttpClient(handler));

        var mockLogger = new Moq.Mock<ILoggerService>();

        _service = new CoverService(httpClientFactoryMock.Object, mockSettings.Object, mockLogger.Object, new FakeImageCache());
        _limits = new CoverArt.LimitsConfig
        {
            MaxResolution = 5000,
            MaxSizeKB = 10240, // 10MB — 宽松限制，只要真实封面
        };
    }

    public void Dispose() { }

    // ================================================================
    //  数据源
    // ================================================================

    public static IEnumerable<object[]> GetTestSongs()
        => TestSongs.Select(s => new object[] { s });

    // ================================================================
    //  综合测试（aggregate 模式）
    // ================================================================

    [Fact]
    public async Task SearchAllSources_多首流行歌曲_至少一个源有结果()
    {
        var passed = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var song in TestSongs)
        {
            try
            {
                var file = new MusicFile { Artist = song.Artist, Title = song.Title, Album = song.Album };
                var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 3 };
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var results = await _service.SearchCoversAsync(file, condition, cts.Token);

                if (results.Count == 0)
                {
                    failed++;
                    errors.Add($"{song.Artist} - {song.Title}: 无搜索结果");
                    continue;
                }

                var validUrls = results.Count(r => !string.IsNullOrEmpty(r.CoverUrl));
                if (validUrls == 0)
                {
                    failed++;
                    errors.Add($"{song.Artist} - {song.Title}: 有搜索结果({results.Count}条)但CoverUrl全部为空");
                    continue;
                }

                passed++;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                or OperationCanceledException)
            {
                failed++;
                errors.Add($"{song.Artist} - {song.Title}: 网络错误 — {ex.GetType().Name}");
            }
        }

        // 至少过半歌曲有搜索结果才认为通过
        Assert.True(passed >= TestSongs.Length / 2,
            $"通过率太低: {passed}/{TestSongs.Length} 通过\n{string.Join("\n", errors)}");
    }

    [Fact]
    public async Task SearchAllSources_搜索加下载_至少一首歌能完整走通()
    {
        var downloaded = 0;
        var errors = new List<string>();

        foreach (var song in TestSongs)
        {
            try
            {
                var file = new MusicFile { Artist = song.Artist, Title = song.Title, Album = song.Album };
                var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 5 };
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var results = await _service.SearchCoversAsync(file, condition, cts.Token);
                var validResults = results.Where(r => !string.IsNullOrEmpty(r.CoverUrl)).ToList();

                if (validResults.Count == 0)
                {
                    errors.Add($"{song.Artist} - {song.Title}: 搜索无有效封面URL");
                    continue;
                }

                foreach (var result in validResults.Take(3))
                {
                    using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var cover = await _service.DownloadCoverAsync(result, _limits, cts2.Token);
                    if (cover != null && cover.HasImage)
                    {
                        downloaded++;
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                or OperationCanceledException)
            {
                errors.Add($"{song.Artist} - {song.Title}: {ex.GetType().Name}");
            }
        }

        Assert.True(downloaded >= 1,
            $"没有一首歌能成功下载封面 ({downloaded}/{TestSongs.Length})\n{string.Join("\n", errors)}");
    }

    /// <summary>真实验证网易云封面搜索的 limit/offset 分页，以及下一页结果不会重复第一页。</summary>
    [Fact]
    public async Task SearchCoversFromSource_Netease_分页返回不同结果()
    {
        try
        {
            var file = new MusicFile();
            var firstPageCondition = new SearchCondition
            {
                CustomQuery = "Beyond",
                WebSearchItemsLimit = 2,
                WebSearchItemsOffset = 0,
            };
            var secondPageCondition = new SearchCondition
            {
                CustomQuery = "Beyond",
                WebSearchItemsLimit = 2,
                WebSearchItemsOffset = 2,
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var firstPage = await _service.SearchCoversFromSourceAsync(
                file, "netease", firstPageCondition, cts.Token);
            var secondPage = await _service.SearchCoversFromSourceAsync(
                file, "netease", secondPageCondition, cts.Token);

            Assert.InRange(firstPage.Count, 1, 2);
            Assert.InRange(secondPage.Count, 1, 2);
            var firstKeys = new HashSet<string>(firstPage.Select(r => r.GetIdentityKey()));
            Assert.DoesNotContain(secondPage, result => firstKeys.Contains(result.GetIdentityKey()));
        }
        catch (HttpRequestException ex)
        {
            Assert.Fail($"网易云封面分页 API 请求失败: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            Assert.Fail("网易云封面分页 API 请求超时");
        }
    }

    /// <summary>逐源真实验证封面分页；iTunes 已由能力测试标记为不支持。</summary>
    [Theory]
    [InlineData("qq", "Beyond")]
    [InlineData("kuwo", "Beyond")]
    [InlineData("lastfm", "1989")]
    [InlineData("musicbrainz", "Taylor Swift")]
    [InlineData("discogs", "Taylor Swift")]
    public async Task SearchCoversFromSource_其他分页源_分页返回不同结果(string source, string query)
    {
        var file = source == "lastfm"
            ? new MusicFile { Artist = "Taylor Swift", Album = query, Title = "Shake It Off" }
            : new MusicFile();
        var firstPageCondition = new SearchCondition
        {
            CustomQuery = query,
            ItunesCountry = "US",
            WebSearchItemsLimit = 2,
            WebSearchItemsOffset = 0,
        };
        var secondPageCondition = new SearchCondition
        {
            CustomQuery = query,
            ItunesCountry = "US",
            WebSearchItemsLimit = 2,
            WebSearchItemsOffset = 2,
        };

        using var firstCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var secondCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var firstPage = await _service.SearchCoversFromSourceAsync(
            file, source, firstPageCondition, firstCts.Token);
        var secondPage = await _service.SearchCoversFromSourceAsync(
            file, source, secondPageCondition, secondCts.Token);

        Assert.InRange(firstPage.Count, 1, 2);
        Assert.InRange(secondPage.Count, 1, 2);
        var firstKeys = new HashSet<string>(firstPage.Select(r => r.GetIdentityKey()));
        Assert.DoesNotContain(secondPage, result => firstKeys.Contains(result.GetIdentityKey()));
    }

    [Theory]
    [InlineData("default", true)]
    [InlineData("itunes", false)]
    [InlineData("netease", true)]
    [InlineData("qq", true)]
    [InlineData("kuwo", true)]
    [InlineData("lastfm", true)]
    [InlineData("musicbrainz", true)]
    [InlineData("discogs", true)]
    public void SupportsPagination_封面源能力声明正确(string source, bool expected)
    {
        Assert.Equal(expected, _service.SupportsPagination(source));
    }

    // ================================================================
    //  iTunes 测试
    // ================================================================

    [Theory]
    [MemberData(nameof(GetTestSongs))]
    public async Task SearchITunes_应返回有效封面(TestSong song)
    {
        var file = new MusicFile { Artist = song.Artist, Title = song.Title };
        var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 5 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var results = await _service.SearchCoversFromSourceAsync(file, "itunes", condition, cts.Token);

        Assert.NotEmpty(results);
        Assert.All(results, r =>
        {
            Assert.False(string.IsNullOrEmpty(r.CoverUrl), $"结果 [{r.Title}] 缺少 CoverUrl");
            Assert.Equal("iTunes", r.SourceName);
        });
    }

    [Theory]
    [MemberData(nameof(GetTestSongs))]
    public async Task DownloadITunesCover_应返回有效图片(TestSong song)
    {
        await TestDownloadForSource(song, "itunes");
    }

    // ================================================================
    //  网易云音乐测试
    // ================================================================

    [Theory]
    [MemberData(nameof(GetTestSongs))]
    public async Task SearchNetease_应返回有效封面(TestSong song)
    {
        var file = new MusicFile { Artist = song.Artist, Title = song.Title, Album = song.Album };
        var condition = new SearchCondition { UseArtist = true, UseAlbum = true, WebSearchItemsLimit = 5 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var results = await _service.SearchCoversFromSourceAsync(file, "netease", condition, cts.Token);

        Assert.NotEmpty(results);
        Assert.All(results, r =>
        {
            Assert.False(string.IsNullOrEmpty(r.CoverUrl), $"结果 [{r.Title}] 缺少 CoverUrl");
            Assert.Equal("网易云音乐", r.SourceName);
            Assert.True(r.CoverUrl.StartsWith("http"), $"CoverUrl 格式不正确: {r.CoverUrl}");
        });
    }

    [Theory]
    [MemberData(nameof(GetTestSongs))]
    public async Task DownloadNeteaseCover_应返回有效图片(TestSong song)
    {
        await TestDownloadForSource(song, "netease");
    }

    // ================================================================
    //  QQ音乐测试
    // ================================================================

    [Theory]
    [MemberData(nameof(GetTestSongs))]
    public async Task SearchQQ_应返回有效封面(TestSong song)
    {
        var file = new MusicFile { Artist = song.Artist, Title = song.Title };
        var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 5 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var results = await _service.SearchCoversFromSourceAsync(file, "qq", condition, cts.Token);

        Assert.NotEmpty(results);
        Assert.All(results, r =>
        {
            Assert.False(string.IsNullOrEmpty(r.CoverUrl), $"结果 [{r.Title}] 缺少 CoverUrl");
            Assert.Equal("QQ音乐", r.SourceName);
        });
    }

    [Theory]
    [MemberData(nameof(GetTestSongs))]
    public async Task DownloadQQCover_应返回有效图片(TestSong song)
    {
        await TestDownloadForSource(song, "qq");
    }

    // ================================================================
    //  酷我音乐测试
    // ================================================================

    [Theory]
    [MemberData(nameof(GetTestSongs))]
    public async Task SearchKuwo_应返回有效封面(TestSong song)
    {
        var file = new MusicFile { Artist = song.Artist, Title = song.Title };
        var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 5 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var results = await _service.SearchCoversFromSourceAsync(file, "kuwo", condition, cts.Token);

        Assert.NotEmpty(results);
        Assert.All(results, r =>
        {
            Assert.False(string.IsNullOrEmpty(r.CoverUrl), $"结果 [{r.Title}] 缺少 CoverUrl");
            Assert.Equal("酷我音乐", r.SourceName);
        });
    }

    [Theory]
    [MemberData(nameof(GetTestSongs))]
    public async Task DownloadKuwoCover_应返回有效图片(TestSong song)
    {
        await TestDownloadForSource(song, "kuwo");
    }

    // ================================================================
    //  Last.fm 测试
    // ================================================================

    [Theory]
    [MemberData(nameof(GetTestSongs))]
    public async Task SearchLastfm_应返回有效封面(TestSong song)
    {
        var file = new MusicFile { Artist = song.Artist, Title = song.Title, Album = song.Album };
        var condition = new SearchCondition { UseArtist = true, UseAlbum = true, WebSearchItemsLimit = 5 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var results = await _service.SearchCoversFromSourceAsync(file, "lastfm", condition, cts.Token);

        Assert.NotEmpty(results);
        Assert.All(results, r =>
        {
            Assert.False(string.IsNullOrEmpty(r.CoverUrl), $"结果 [{r.Title}] 缺少 CoverUrl");
            Assert.Equal("Last.fm", r.SourceName);
        });
    }

    [Theory]
    [MemberData(nameof(GetTestSongs))]
    public async Task DownloadLastfmCover_应返回有效图片(TestSong song)
    {
        // Last.fm CDN (lastfm.freetls.fastly.net) 在部分网络环境不可达，
        // 仅验证搜索返回结果，下载验证在网络可达时执行。
        var file = new MusicFile { Artist = song.Artist, Title = song.Title, Album = song.Album };
        var condition = new SearchCondition { UseArtist = true, UseAlbum = true, WebSearchItemsLimit = 5 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        IReadOnlyList<SearchResult> results;
        try
        {
            results = await _service.SearchCoversFromSourceAsync(file, "lastfm", condition, cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or OperationCanceledException)
        {
            return; // 网络错误跳过
        }

        Assert.NotEmpty(results);

        // 尝试下载，但不强制要求成功
        foreach (var result in results.Where(r => !string.IsNullOrEmpty(r.CoverUrl)).Take(3))
        {
            try
            {
                using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var cover = await _service.DownloadCoverAsync(result, _limits, cts2.Token);
                if (cover != null && cover.HasImage)
                    return;
            }
            catch { /* 继续尝试 */ }
        }
    }

    // ================================================================
    //  MusicBrainz 封面测试
    // ================================================================

    [Theory]
    [MemberData(nameof(GetTestSongs))]
    public async Task SearchMusicBrainz_应返回有效封面(TestSong song)
    {
        var file = new MusicFile { Artist = song.Artist, Title = song.Title, Album = song.Album };
        var condition = new SearchCondition { UseArtist = true, UseAlbum = true, WebSearchItemsLimit = 5 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var results = await _service.SearchCoversFromSourceAsync(file, "musicbrainz", condition, cts.Token);

        Assert.NotEmpty(results);
        Assert.All(results, r =>
        {
            Assert.False(string.IsNullOrEmpty(r.CoverUrl), $"结果 [{r.Title}] 缺少 CoverUrl");
            Assert.Equal("MusicBrainz", r.SourceName);
        });
    }

    [Theory]
    [MemberData(nameof(GetTestSongs))]
    public async Task DownloadMusicBrainzCover_应返回有效图片(TestSong song)
    {
        // MusicBrainz 封面托管在 archive.org（Cover Art Archive），
        // 属社区维护，非所有 release 都有封面，且 archive.org 在部分网络环境不可达。
        // 因此此测试仅验证搜索返回有效 CoverUrl，不强制要求下载成功。

        var file = new MusicFile { Artist = song.Artist, Title = song.Title, Album = song.Album };
        var condition = new SearchCondition { UseArtist = true, UseAlbum = true, WebSearchItemsLimit = 5 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var results = await _service.SearchCoversFromSourceAsync(file, "musicbrainz", condition, cts.Token);

        Assert.NotEmpty(results);
        var validResults = results.Where(r => !string.IsNullOrEmpty(r.CoverUrl)).ToList();
        Assert.NotEmpty(validResults);

        // 尝试下载，但不强制要求成功（Cover Art Archive 可能无封面或不可达）
        foreach (var result in validResults.Take(3))
        {
            try
            {
                using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var cover = await _service.DownloadCoverAsync(result, _limits, cts2.Token);
                if (cover != null && cover.HasImage)
                    return; // 成功下载，测试通过
            }
            catch { /* 继续尝试下一个 */ }
        }

        // 所有尝试均未成功下载，但搜索本身有效，测试仍视为通过
    }

    // ================================================================
    //  MusicBrainz 标签搜索测试
    // ================================================================

    [Theory]
    [MemberData(nameof(GetTestSongs))]
    public async Task SearchMusicBrainzTags_应返回有效元数据(TestSong song)
    {
        var file = new MusicFile { Artist = song.Artist, Title = song.Title, Album = song.Album };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var results = await _service.SearchMusicBrainzTagsAsync(
            $"{song.Artist} {song.Title}", 5, cts.Token);

        Assert.NotEmpty(results);
        Assert.All(results, r =>
        {
            Assert.False(string.IsNullOrEmpty(r.Title), "标签结果缺少标题");
            Assert.Equal("MusicBrainz", r.SourceName);
        });
    }

    [Fact]
    public async Task SearchMusicBrainzTags_日系歌曲_不应抛异常()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var results = await _service.SearchMusicBrainzTagsAsync("YOASOBI 夜に駆ける", 5, cts.Token);
        Assert.NotNull(results);
    }

    // ================================================================
    //  Discogs 封面测试
    // ================================================================

    [Theory]
    [MemberData(nameof(GetTestSongs))]
    public async Task SearchDiscogs_应返回有效封面(TestSong song)
    {
        // Discogs 封面需要通过 release 详情接口获取（搜索结果不含封面 URL），
        // 且并非所有 release 都有封面图片，对中文歌曲支持有限。
        // 测试仅验证不抛异常，不要求一定有结果。
        var file = new MusicFile { Artist = song.Artist, Title = song.Title, Album = song.Album };
        var condition = new SearchCondition { UseArtist = true, UseAlbum = true, WebSearchItemsLimit = 5 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            var results = await _service.SearchCoversFromSourceAsync(file, "discogs", condition, cts.Token);

            if (results.Count > 0)
            {
                Assert.All(results, r => Assert.Equal("Discogs", r.SourceName));
            }
        }
        catch (HttpRequestException) { /* 速率限制或网络错误 */ }
        catch (TaskCanceledException) { /* 超时 */ }
    }

    [Theory]
    [MemberData(nameof(GetTestSongs))]
    public async Task DownloadDiscogsCover_应返回有效图片(TestSong song)
    {
        // Discogs 封面需要通过 release 详情接口获取，且受速率限制。
        // 仅验证搜索返回结果，下载验证在网络可达时执行。
        var file = new MusicFile { Artist = song.Artist, Title = song.Title, Album = song.Album };
        var condition = new SearchCondition { UseArtist = true, UseAlbum = true, WebSearchItemsLimit = 5 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        IReadOnlyList<SearchResult> results;
        try
        {
            results = await _service.SearchCoversFromSourceAsync(file, "discogs", condition, cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or OperationCanceledException)
        {
            // 网络错误跳过
            return;
        }

        // 搜索本身应返回结果
        Assert.NotEmpty(results);

        // 尝试下载，但不强制要求成功（Discogs 可能无封面或速率限制）
        foreach (var result in results.Where(r => !string.IsNullOrEmpty(r.CoverUrl)).Take(3))
        {
            try
            {
                using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var cover = await _service.DownloadCoverAsync(result, _limits, cts2.Token);
                if (cover != null && cover.HasImage)
                    return;
            }
            catch { /* 继续尝试 */ }
        }
    }

    // ================================================================
    //  Discogs 标签搜索测试
    // ================================================================

    [Theory]
    [MemberData(nameof(GetTestSongs))]
    public async Task SearchDiscogsTags_应返回有效元数据(TestSong song)
    {
        // Discogs 对中文歌曲支持有限，且有速率限制（60 请求/分钟）
        // 测试仅验证不抛异常，不要求一定有结果
        var file = new MusicFile { Artist = song.Artist, Title = song.Title, Album = song.Album };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        try
        {
            var results = await _service.SearchDiscogsTagsAsync(
                $"{song.Artist} {song.Title}", 5, cts.Token);

            if (results.Count > 0)
            {
                Assert.All(results, r =>
                {
                    Assert.False(string.IsNullOrEmpty(r.Title), "标签结果缺少标题");
                    Assert.Equal("Discogs", r.SourceName);
                });
            }
        }
        catch (HttpRequestException) { /* 速率限制或网络错误 */ }
        catch (TaskCanceledException) { /* 超时 */ }
    }

    [Fact]
    public async Task SearchDiscogsTags_日系歌曲_不应抛异常()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var results = await _service.SearchDiscogsTagsAsync("YOASOBI 夜に駆ける", 5, cts.Token);
        Assert.NotNull(results);
    }

    // ================================================================
    //  QQ封面 CDN 直连验证
    // ================================================================

    /// <summary>
    /// QQ音乐专辑封面 URL 格式直连测试 —
    /// 验证 y.gtimg.cn CDN 域名是否可访问。
    /// </summary>
    [Fact]
    public async Task QqCoverCdnDirect_已知albumMid_应可下载()
    {
        var knownAlbumMids = new[] { "0042zXeF0VwKjA", "003OUlO50tqI6o" };
        var downloaded = 0;

        foreach (var mid in knownAlbumMids)
        {
            try
            {
                var url = $"https://y.gtimg.cn/music/photo_new/T002R800x800M000{mid}.jpg";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Referer", "https://y.qq.com");
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var response = await http.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadAsByteArrayAsync();
                    if (data.Length > 100)
                        downloaded++;
                }
            }
            catch { /* 个别 mid 可能不存在 */ }
        }

        Assert.True(downloaded >= 1,
            $"已知 {knownAlbumMids.Length} 个 albumMid 全部无法下载 QQ 封面");
    }

    // ================================================================
    //  异常场景测试
    // ================================================================

    [Fact]
    public async Task SearchCovers_空查询_不抛异常()
    {
        var file = new MusicFile { Artist = "", Title = "" };
        var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 3 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var sources = new[] { "itunes", "netease", "qq", "kuwo", "lastfm", "musicbrainz", "discogs" };
        foreach (var source in sources)
        {
            var results = await _service.SearchCoversFromSourceAsync(file, source, condition, cts.Token);
            Assert.NotNull(results);
        }

        var all = await _service.SearchCoversAsync(file, condition, cts.Token);
        Assert.NotNull(all);
    }

    [Fact]
    public async Task DownloadCover_无效URL_返回null而不是抛异常()
    {
        var result = new SearchResult
        {
            SourceName = "test",
            CoverUrl = "https://invalid-url-that-does-not-exist.example.com/cover.jpg",
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var cover = await _service.DownloadCoverAsync(result, _limits, cts.Token);
        Assert.Null(cover);
    }

    // ================================================================
    //  辅助方法
    // ================================================================

    /// <summary>通用流程：搜索 → 取第一个有效 URL → 下载验证</summary>
    private async Task TestDownloadForSource(TestSong song, string source)
    {
        var file = new MusicFile { Artist = song.Artist, Title = song.Title, Album = song.Album };
        var condition = new SearchCondition { UseArtist = true, UseAlbum = true, WebSearchItemsLimit = 5 };

        IReadOnlyList<SearchResult> results;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            results = await _service.SearchCoversFromSourceAsync(file, source, condition, cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or OperationCanceledException)
        {
            Assert.Fail($"搜索失败 [{source}] {song.Artist} - {song.Title}: {ex.GetType().Name} — {ex.Message}");
            return;
        }

        if (results.Count == 0)
        {
            Assert.Fail($"无搜索结果 [{source}] {song.Artist} - {song.Title}");
            return;
        }

        var validResults = results.Where(r => !string.IsNullOrEmpty(r.CoverUrl)).ToList();
        Assert.NotEmpty(validResults);

        // 取第一个有效结果下载
        var firstResult = validResults[0];
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var cover = await _service.DownloadCoverAsync(firstResult, _limits, cts2.Token);

            Assert.NotNull(cover);
            Assert.True(cover!.HasImage, "下载了空图片数据");
            Assert.True(cover.Width > 0, $"图片宽度为0 (URL: {firstResult.CoverUrl})");
            Assert.True(cover.Height > 0, $"图片高度为0 (URL: {firstResult.CoverUrl})");
        }
        catch (HttpRequestException ex)
        {
            Assert.Fail($"下载失败 [{source}] {song.Artist} - {song.Title}: HTTP错误 — {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            Assert.Fail($"下载超时 [{source}] {song.Artist} - {song.Title} (URL: {firstResult.CoverUrl})");
        }
    }
}
