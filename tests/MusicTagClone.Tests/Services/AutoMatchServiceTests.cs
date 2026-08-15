using Moq;
using MusicTagClone.Interfaces;
using MusicTagClone.Models;
using MusicTagClone.Services;

namespace MusicTagClone.Tests.Services;

public sealed class AutoMatchServiceTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "automatch_" + Guid.NewGuid() + ".mp3");

    public AutoMatchServiceTests() => File.WriteAllBytes(_file, new byte[] { 1 });
    public void Dispose() { if (File.Exists(_file)) File.Delete(_file); }

    [Theory]
    [InlineData(false, "Old title")]
    [InlineData(true, "Matched title")]
    public async Task Title_RespectsOverwriteOption(bool overwrite, string expected)
    {
        var settings = CreateSettings();
        var tag = new Mock<ITagService>();
        tag.Setup(s => s.ReadTagsAsync(_file)).ReturnsAsync(new TagData { Title = "Old title" });
        TagData? written = null;
        tag.Setup(s => s.WriteTagsAsync(_file, It.IsAny<TagData>(), false))
            .Callback<string, TagData, bool>((_, value, _) => written = value)
            .ReturnsAsync(true);
        var cover = new Mock<ICoverService>();
        cover.Setup(s => s.SearchCoversFromSourceAsync(It.IsAny<MusicFile>(), It.IsAny<string>(), It.IsAny<SearchCondition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new SearchResult { Title = "Matched title", MatchScore = 1 } });
        var service = new AutoMatchService(settings.Object, tag.Object,
            Mock.Of<ILyricService>(), cover.Object, Mock.Of<ILoggerService>());
        var options = TextOnlyOptions(overwrite);

        await service.ExecuteAsync(new[] { MusicFile.FromPath(_file) }, options);

        if (overwrite) Assert.Equal(expected, written?.Title);
        else Assert.Null(written);
    }

    [Fact]
    public async Task CoverOnly_TagMode_PersistsAllPictures()
    {
        var settings = CreateSettings();
        var tag = new Mock<ITagService>();
        tag.Setup(s => s.ReadTagsAsync(_file)).ReturnsAsync(new TagData());
        TagData? written = null;
        tag.Setup(s => s.WriteTagsAsync(_file, It.IsAny<TagData>(), false))
            .Callback<string, TagData, bool>((_, value, _) => written = value)
            .ReturnsAsync(true);

        var cover = new Mock<ICoverService>();
        cover.Setup(s => s.SearchCoversFromSourceAsync(It.IsAny<MusicFile>(), It.IsAny<string>(),
                It.IsAny<SearchCondition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new SearchResult { Title = "Matched", CoverUrl = "https://example.test/cover.jpg" } });
        cover.Setup(s => s.DownloadCoverAsync(It.IsAny<SearchResult>(), It.IsAny<CoverArt.LimitsConfig>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CoverArt { ImageData = new byte[] { 1, 2, 3 }, MimeType = "image/jpeg" });

        var service = new AutoMatchService(settings.Object, tag.Object,
            Mock.Of<ILyricService>(), cover.Object, Mock.Of<ILoggerService>());
        var options = new AutoMatchOptions();
        foreach (var field in options.Fields.Values) field.Enabled = false;
        options.Get(AutoMatchOptions.Cover).Enabled = true;

        var result = await service.ExecuteAsync(new[] { MusicFile.FromPath(_file) }, options);

        Assert.True(result.WrittenCount == 1);
        Assert.NotNull(written?.AllPictures);
        Assert.Single(written!.AllPictures!);
    }

    [Fact]
    public async Task CoverDownloadFailure_FallsBackToNextSource()
    {
        var settings = CreateSettings();
        var tag = new Mock<ITagService>();
        tag.Setup(s => s.ReadTagsAsync(_file)).ReturnsAsync(new TagData());
        TagData? written = null;
        tag.Setup(s => s.WriteTagsAsync(_file, It.IsAny<TagData>(), false))
            .Callback<string, TagData, bool>((_, value, _) => written = value)
            .ReturnsAsync(true);

        var cover = new Mock<ICoverService>();
        cover.Setup(s => s.SearchCoversFromSourceAsync(It.IsAny<MusicFile>(), It.IsAny<string>(),
                It.IsAny<SearchCondition>(), It.IsAny<CancellationToken>()))
            .Returns((MusicFile _, string source, SearchCondition _, CancellationToken _) =>
                Task.FromResult<IReadOnlyList<SearchResult>>(source == "netease"
                    ? new[] { new SearchResult { SourceName = "失效源", CoverUrl = "https://example.test/broken.jpg", MatchScore = 1.0 } }
                    : new[] { new SearchResult { SourceName = "备用源", CoverUrl = "https://example.test/fallback.jpg", MatchScore = 0.5 } }));
        var downloadedSources = new List<string>();
        cover.Setup(s => s.DownloadCoverAsync(It.IsAny<SearchResult>(), It.IsAny<CoverArt.LimitsConfig>(),
                It.IsAny<CancellationToken>()))
            .Returns((SearchResult result, CoverArt.LimitsConfig _, CancellationToken _) =>
            {
                downloadedSources.Add(result.SourceName);
                return Task.FromResult<CoverArt?>(result.SourceName == "失效源"
                    ? null
                    : new CoverArt { ImageData = new byte[] { 1, 2, 3 }, MimeType = "image/jpeg" });
            });

        var service = new AutoMatchService(settings.Object, tag.Object,
            Mock.Of<ILyricService>(), cover.Object, Mock.Of<ILoggerService>());
        var options = new AutoMatchOptions();
        foreach (var field in options.Fields.Values) field.Enabled = false;
        options.Get(AutoMatchOptions.Cover).Enabled = true;

        await service.ExecuteAsync(new[] { MusicFile.FromPath(_file) }, options);

        Assert.Equal(new[] { "失效源", "备用源" }, downloadedSources);
        Assert.NotNull(written?.AllPictures);
        Assert.Single(written!.AllPictures!);
    }

    [Fact]
    public async Task CoverOnly_UsesCombinationSourceAsFallback()
    {
        var settings = CreateSettings();
        settings.SetupGet(s => s.PictureInfo_SourceItemList).Returns(OnlySourceJson(0));
        settings.SetupGet(s => s.CombTagsInfo_SourceItemList).Returns(OnlySourceJson(1));
        var tag = new Mock<ITagService>();
        tag.Setup(s => s.ReadTagsAsync(_file)).ReturnsAsync(new TagData());
        TagData? written = null;
        tag.Setup(s => s.WriteTagsAsync(_file, It.IsAny<TagData>(), false))
            .Callback<string, TagData, bool>((_, value, _) => written = value)
            .ReturnsAsync(true);

        var cover = new Mock<ICoverService>();
        cover.Setup(s => s.SearchCoversFromSourceAsync(It.IsAny<MusicFile>(), It.IsAny<string>(),
                It.IsAny<SearchCondition>(), It.IsAny<CancellationToken>()))
            .Returns((MusicFile _, string source, SearchCondition _, CancellationToken _) =>
                Task.FromResult<IReadOnlyList<SearchResult>>(source == "netease"
                    ? new[] { new SearchResult { SourceName = "图片失效源", CoverUrl = "https://example.test/broken.jpg", MatchScore = 1.0 } }
                    : new[] { new SearchResult { SourceName = "组合备用源", CoverUrl = "https://example.test/fallback.jpg", MatchScore = 0.5 } }));
        var downloadedSources = new List<string>();
        cover.Setup(s => s.DownloadCoverAsync(It.IsAny<SearchResult>(), It.IsAny<CoverArt.LimitsConfig>(),
                It.IsAny<CancellationToken>()))
            .Returns((SearchResult result, CoverArt.LimitsConfig _, CancellationToken _) =>
            {
                downloadedSources.Add(result.SourceName);
                return Task.FromResult<CoverArt?>(result.SourceName == "图片失效源"
                    ? null
                    : new CoverArt { ImageData = new byte[] { 1, 2, 3 }, MimeType = "image/jpeg" });
            });

        var service = new AutoMatchService(settings.Object, tag.Object,
            Mock.Of<ILyricService>(), cover.Object, Mock.Of<ILoggerService>());
        var options = new AutoMatchOptions();
        foreach (var field in options.Fields.Values) field.Enabled = false;
        options.Get(AutoMatchOptions.Cover).Enabled = true;

        await service.ExecuteAsync(new[] { MusicFile.FromPath(_file) }, options);

        Assert.Equal(new[] { "图片失效源", "组合备用源" }, downloadedSources);
        Assert.NotNull(written?.AllPictures);
        Assert.Single(written!.AllPictures!);
    }

    [Fact]
    public async Task EmptyTags_SearchesWithCompleteFilename()
    {
        var settings = CreateSettings();
        settings.SetupGet(s => s.SearchConditionUseTitle).Returns(false);
        settings.SetupGet(s => s.SearchConditionUseArtist).Returns(false);
        settings.SetupGet(s => s.SearchConditionUseAlbum).Returns(false);
        var tag = new Mock<ITagService>();
        tag.Setup(s => s.ReadTagsAsync(_file)).ReturnsAsync(new TagData());
        tag.Setup(s => s.WriteTagsAsync(_file, It.IsAny<TagData>(), false)).ReturnsAsync(true);
        string? query = null;
        var cover = new Mock<ICoverService>();
        cover.Setup(s => s.SearchCoversFromSourceAsync(It.IsAny<MusicFile>(), It.IsAny<string>(),
                It.IsAny<SearchCondition>(), It.IsAny<CancellationToken>()))
            .Returns((MusicFile file, string _, SearchCondition condition, CancellationToken _) =>
            {
                query = condition.BuildSearchQuery(file);
                return Task.FromResult<IReadOnlyList<SearchResult>>(new[]
                {
                    new SearchResult { Title = "Matched title", MatchScore = 1 },
                });
            });
        var service = new AutoMatchService(settings.Object, tag.Object,
            Mock.Of<ILyricService>(), cover.Object, Mock.Of<ILoggerService>());

        await service.ExecuteAsync(new[] { MusicFile.FromPath(_file) }, TextOnlyOptions(true));

        Assert.Equal(Path.GetFileNameWithoutExtension(_file), query);
    }

    private static AutoMatchOptions TextOnlyOptions(bool overwrite)
    {
        var options = new AutoMatchOptions();
        foreach (var field in options.Fields.Values) field.Enabled = false;
        options.Get(AutoMatchOptions.Title).Enabled = true;
        options.Get(AutoMatchOptions.Title).Overwrite = overwrite;
        return options;
    }

    private static Mock<ISettingsService> CreateSettings()
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.SearchConditionUseTitle).Returns(true);
        settings.SetupGet(s => s.SearchConditionUseArtist).Returns(true);
        settings.SetupGet(s => s.SearchConditionUseAlbum).Returns(false);
        settings.SetupGet(s => s.WebSearchItemsLimit).Returns(10);
        settings.SetupGet(s => s.ItunesSearchParamsCountry).Returns("CN");
        settings.SetupGet(s => s.PictureFormatLimits).Returns("jpg,png");
        settings.SetupGet(s => s.PictureResolutionLimits).Returns(3000);
        settings.SetupGet(s => s.PictureSizeLimitsKB).Returns(1024);
        return settings;
    }

    private static string OnlySourceJson(int source)
    {
        var items = new[] { 0, 1, 3, 5, 6, 7, 9 }
            .Select((value, index) => string.Format(
                "{{\"Src\":{0},\"Enabled\":{1},\"Seq\":{2}}}",
                value, value == source ? "true" : "false", index));
        return "[" + string.Join(",", items) + "]";
    }
}
