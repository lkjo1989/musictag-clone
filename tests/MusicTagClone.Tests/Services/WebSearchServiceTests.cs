using Moq;
using MusicTagClone.Interfaces;
using MusicTagClone.Models;
using MusicTagClone.Services;

namespace MusicTagClone.Tests.Services;

/// <summary>
/// 综合搜索服务测试 — 在线API测试
/// </summary>
public class WebSearchServiceTests
{
    private readonly WebSearchService _service;
    private readonly Mock<ISettingsService> _mockSettings;

    public WebSearchServiceTests()
    {
        _mockSettings = new Mock<ISettingsService>();
        _mockSettings.Setup(s => s.ItunesSearchParamsCountry).Returns("CN");
        _mockSettings.Setup(s => s.WebSearchItemsLimit).Returns(10);
        _mockSettings.Setup(s => s.SearchConditionUseTitle).Returns(true);
        _mockSettings.Setup(s => s.SearchConditionUseArtist).Returns(true);
        _mockSettings.Setup(s => s.SearchConditionUseAlbum).Returns(true);
        _mockSettings.Setup(s => s.SearchConditionUseOnlyFilename).Returns(false);
        _mockSettings.Setup(s => s.AutoMatchTagsWebSearchThreadCount).Returns(4);

        var httpClient = new HttpClient();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient("default")).Returns(httpClient);
        var mockLogger = new Mock<ILoggerService>();
        var lyricService = new LyricService(httpClientFactoryMock.Object, _mockSettings.Object, mockLogger.Object);
        var coverService = new CoverService(httpClientFactoryMock.Object, _mockSettings.Object, mockLogger.Object, new FakeImageCache());
        _service = new WebSearchService(lyricService, coverService, _mockSettings.Object);
    }

    [Fact]
    public async Task AutoMatchTagsAsync_ReturnsResults()
    {
        var file = new MusicFile { Artist = "周杰伦", Title = "晴天" };

        try
        {
            var results = await _service.AutoMatchTagsAsync(file,
                new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);

            // 应该有来自不同源的结果
            Assert.NotEmpty(results);
        }
        catch (HttpRequestException)
        {
            Assert.Fail("待解决: 搜索API不可达");
        }
        catch (TaskCanceledException)
        {
            Assert.Fail("待解决: 搜索API响应超时");
        }
    }

    [Fact]
    public async Task BatchAutoMatchAsync_MultipleFiles_AllProcessed()
    {
        var files = new List<MusicFile>
        {
            new() { Artist = "周杰伦", Title = "晴天" },
            new() { Artist = "林俊杰", Title = "江南" },
        };

        try
        {
            var progressValues = new List<int>();
            var progress = new Progress<int>(p => progressValues.Add(p));

            var results = await _service.BatchAutoMatchAsync(files, progress);

            // 所有文件都应被处理（即使某些搜索无结果）
            // results 可能少于文件数，因为无结果的文件不加入字典
            Assert.True(results.Count >= 1);
            Assert.Contains(2, progressValues); // 最终进度应等于文件数
        }
        catch (HttpRequestException)
        {
            // 批量搜索API不可达，跳过
        }
        catch (TaskCanceledException)
        {
            // 批量搜索API响应超时，跳过
        }
    }
}
