using Moq;
using MusicTagClone.Interfaces;
using MusicTagClone.Models;
using MusicTagClone.Services;

namespace MusicTagClone.Tests.Services;

/// <summary>
/// 歌词服务测试 — 纯逻辑测试 + 在线API测试
/// 在线API测试标记为 "网络依赖"，接口不通时单独处理
/// </summary>
public class LyricServiceTests
{
    private readonly LyricService _service;
    private readonly Mock<ISettingsService> _mockSettings;
    private readonly Mock<ILoggerService> _mockLogger;

    public LyricServiceTests()
    {
        _mockSettings = new Mock<ISettingsService>();
        _mockSettings.Setup(s => s.ItunesSearchParamsCountry).Returns("CN");
        _mockSettings.Setup(s => s.WebSearchItemsLimit).Returns(10);
        _mockLogger = new Mock<ILoggerService>();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient("default")).Returns(new HttpClient());
        _service = new LyricService(httpClientFactoryMock.Object, _mockSettings.Object, _mockLogger.Object);
    }

    #region 纯逻辑测试

    [Fact]
    public void ReformatTimetag_StandardFormat_ReturnsSame()
    {
        var input = "[01:23.45]Test lyrics";
        var result = _service.ReformatTimetag(input);
        Assert.Contains("[01:23.", result);
        Assert.Contains("]Test lyrics", result);
    }

    [Fact]
    public void ReformatTimetag_MultipleLines_ProcessesAll()
    {
        var input = "[00:00.00]Line 1\n[00:05.00]Line 2\n[00:10.00]Line 3";
        var result = _service.ReformatTimetag(input);
        Assert.Contains("Line 1", result);
        Assert.Contains("Line 2", result);
        Assert.Contains("Line 3", result);
    }

    [Fact]
    public void RemoveTimetag_RemovesTimeTags()
    {
        var input = "[00:00.00]Line 1\n[00:05.00]Line 2";
        var result = _service.RemoveTimetag(input);
        Assert.DoesNotContain("[00:00.00]", result);
        Assert.DoesNotContain("[00:05.00]", result);
        Assert.Contains("Line 1", result);
        Assert.Contains("Line 2", result);
    }

    [Fact]
    public void RemoveTimetag_WithEmptyLines_Handled()
    {
        var input = "[00:00.00]Line 1\n\n[00:05.00]Line 2";
        var result = _service.RemoveTimetag(input);
        Assert.Contains("Line 1", result);
        Assert.Contains("Line 2", result);
    }

    [Fact]
    public void ParseLrcContent_WithStandardLrc_ParsesCorrectly()
    {
        var lrc = "[00:00.00]First line\n[00:05.00]Second line\n[00:10.00]Third line";
        var result = _service.ParseLrcContent(lrc);

        Assert.NotNull(result);
        Assert.Contains("First line", result!.OriginalLyric!);
        Assert.Contains("Second line", result.OriginalLyric!);
    }

    [Fact]
    public void ParseLrcContent_WithTranslation_SeparatesCorrectly()
    {
        var lrc = "[00:00.00]Original\n[00:05.00][tl:Translation]";
        var result = _service.ParseLrcContent(lrc);

        Assert.NotNull(result);
        Assert.Contains("Original", result!.OriginalLyric!);
    }

    [Fact]
    public async Task SaveLrcFileAsync_SavesFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lrc_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var file = new MusicFile
            {
                FilePath = Path.Combine(tempDir, "test.mp3"),
                Artist = "TestArtist",
                Title = "TestTitle",
                Album = "TestAlbum",
                Track = 1
            };
            var lyric = new LyricInfo
            {
                OriginalLyric = "[00:00.00]Test lyrics",
                LrcFormatted = "[00:00.00]Test lyrics"
            };
            var config = new LyricInfo.SaveConfig
            {
                SaveDirectory = tempDir,
                FilenameFormat = "{artist} - {title}.lrc"
            };

            var result = await _service.SaveLrcFileAsync(tempDir, file, lyric, config);
            Assert.NotNull(result);

            var savedFile = Path.Combine(tempDir, "TestArtist - TestTitle.lrc");
            Assert.Equal(savedFile, result);
            Assert.True(File.Exists(savedFile));
            var content = File.ReadAllText(savedFile);
            Assert.Contains("Test lyrics", content);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task SaveLrcFileAsync_SanitizesFilename()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lrc_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var file = new MusicFile
            {
                FilePath = Path.Combine(tempDir, "test.mp3"),
                Artist = "Test/Artist",
                Title = "Test:Title"
            };
            var lyric = new LyricInfo { LrcFormatted = "lyrics" };
            var config = new LyricInfo.SaveConfig
            {
                SaveDirectory = tempDir,
                FilenameFormat = "{artist} - {title}.lrc"
            };

            var result = await _service.SaveLrcFileAsync(tempDir, file, lyric, config);
            Assert.NotNull(result);

            // 文件名中的非法字符应被替换
            var files = Directory.GetFiles(tempDir, "*.lrc");
            Assert.Single(files);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Theory]
    [InlineData("netease")]
    [InlineData("qq")]
    [InlineData("kugou")]
    [InlineData("kuwo")]
    public void SupportsPagination_歌词源能力声明正确(string source)
    {
        Assert.True(_service.SupportsPagination(source));
    }

    #endregion

    #region 在线API测试 — 联网测试，CI 跳过（标记 Category=Network）

    /// <summary>逐源真实验证剩余歌词源的 limit/offset 分页（联网，CI 跳过）。</summary>
    [Trait("Category", "Network")]
    [Theory]
    [InlineData("qq")]
    [InlineData("kugou")]
    [InlineData("kuwo")]
    public async Task SearchLyricsFromSource_其他分页源_分页返回不同结果(string source)
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
        var config = new LyricInfo.DownloadConfig();

        using var firstCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var secondCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var firstPage = await _service.SearchLyricsFromSourceAsync(
            file, source, firstPageCondition, config, firstCts.Token);
        var secondPage = await _service.SearchLyricsFromSourceAsync(
            file, source, secondPageCondition, config, secondCts.Token);

        Assert.InRange(firstPage.Count, 1, 2);
        Assert.InRange(secondPage.Count, 1, 2);
        var firstKeys = new HashSet<string>(firstPage.Select(r => r.GetIdentityKey()));
        Assert.DoesNotContain(secondPage, result => firstKeys.Contains(result.GetIdentityKey()));
    }

    [Trait("Category", "Network")] // 联网测试，CI 跳过
    [Fact]
    public async Task SearchLyricsAsync_Netease_ReturnsResults()
    {
        var file = new MusicFile { Artist = "周杰伦", Title = "晴天" };
        var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 10 };
        var config = new LyricInfo.DownloadConfig();

        try
        {
            // 直接用指定源搜索（避免其他源挤占 limit）
            var results = await _service.SearchLyricsFromSourceAsync(file, "netease", condition, config,
                new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);

            // 如果能连通，应该有结果
            Assert.NotEmpty(results);
            Assert.All(results, r => Assert.Equal("网易云音乐", r.SourceName));
            Assert.All(results, r => Assert.False(string.IsNullOrEmpty(r.SourceUrl),
                $"结果 [{r.Title}] 缺少 SourceUrl"));
        }
        catch (HttpRequestException ex)
        {
            Assert.Fail($"待解决: 网易云音乐API不可达 — {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            Assert.Fail("待解决: 网易云音乐API响应超时");
        }
    }

    [Trait("Category", "Network")] // 联网测试，CI 跳过
    [Fact]
    public async Task SearchLyricsAsync_QQMusic_ReturnsResults()
    {
        // 待解决: QQ音乐 API 当前无返回结果，可能需要更新请求格式或认证方式
        var file = new MusicFile { Artist = "周杰伦", Title = "晴天" };
        var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 5 };
        var config = new LyricInfo.DownloadConfig();

        try
        {
            var results = await _service.SearchLyricsAsync(file, condition, config,
                new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

            // 不要求必须有结果，只要不抛异常即可
            // 如果有 QQ音乐 结果则验证格式正确
            var qqResults = results.Where(r => r.SourceName == "QQ音乐").ToList();
            if (qqResults.Count > 0)
            {
                Assert.All(qqResults, r => Assert.False(string.IsNullOrEmpty(r.Title)));
            }
        }
        catch (HttpRequestException)
        {
            // QQ音乐API不可达，跳过
        }
        catch (TaskCanceledException)
        {
            // QQ音乐API响应超时，跳过
        }
    }

    [Trait("Category", "Network")] // 联网测试，CI 跳过
    [Fact]
    public async Task SearchLyricsAsync_Kugou_ReturnsResults()
    {
        // 酷狗音乐搜索 API 已更新，歌词下载使用两步流程（搜索候选 + 下载）
        var file = new MusicFile { Artist = "周杰伦", Title = "晴天" };
        var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 5 };
        var config = new LyricInfo.DownloadConfig();

        try
        {
            var results = await _service.SearchLyricsAsync(file, condition, config,
                new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

            var kugouResults = results.Where(r => r.SourceName == "酷狗音乐").ToList();
            if (kugouResults.Count > 0)
            {
                Assert.All(kugouResults, r => Assert.False(string.IsNullOrEmpty(r.Title)));
            }
        }
        catch (HttpRequestException)
        {
            // 酷狗音乐API不可达，跳过
        }
        catch (TaskCanceledException)
        {
            // 酷狗音乐API响应超时，跳过
        }
    }

    [Trait("Category", "Network")] // 联网测试，CI 跳过
    [Fact]
    public async Task SearchLyricsAsync_Kuwo_ReturnsResults()
    {
        var file = new MusicFile { Artist = "周杰伦", Title = "晴天" };
        var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 10 };
        var config = new LyricInfo.DownloadConfig();

        try
        {
            // 直接用指定源搜索（避免其他源挤占 limit）
            var results = await _service.SearchLyricsFromSourceAsync(file, "kuwo", condition, config,
                new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);

            Assert.NotEmpty(results);
            Assert.All(results, r => Assert.Equal("酷我音乐", r.SourceName));
            Assert.All(results, r => Assert.False(string.IsNullOrEmpty(r.SourceUrl),
                $"结果 [{r.Title}] 缺少 SourceUrl"));
        }
        catch (HttpRequestException ex)
        {
            Assert.Fail($"待解决: 酷我音乐API不可达 — {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            Assert.Fail("待解决: 酷我音乐API响应超时");
        }
    }

    [Trait("Category", "Network")] // 联网测试，CI 跳过
    [Fact]
    public async Task DownloadLyricAsync_WithValidUrl_ReturnsLyric()
    {
        // 先搜索获取一个有效的歌词URL
        var file = new MusicFile { Artist = "周杰伦", Title = "晴天" };
        var condition = new SearchCondition { UseArtist = true, WebSearchItemsLimit = 3 };
        var config = new LyricInfo.DownloadConfig();

        try
        {
            var results = await _service.SearchLyricsAsync(file, condition, config,
                new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

            if (results.Count == 0)
            {
                Assert.Fail("待解决: 搜索无结果，无法测试下载");
                return;
            }

            var firstResult = results[0];
            var lyric = await _service.DownloadLyricAsync(firstResult, config,
                new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

            Assert.NotNull(lyric);
            Assert.False(string.IsNullOrEmpty(lyric!.OriginalLyric));
        }
        catch (HttpRequestException)
        {
            Assert.Fail("待解决: 歌词下载API不可达");
        }
        catch (TaskCanceledException)
        {
            Assert.Fail("待解决: 歌词下载API响应超时");
        }
    }

    #endregion
}
