using MusicTagClone.Services;

namespace MusicTagClone.Tests.Services;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDbPath;

    public SettingsServiceTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"musictag_test_{Guid.NewGuid()}.db");
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
            File.Delete(_tempDbPath);
    }

    private SettingsService CreateService() => new(_tempDbPath);

    [Fact]
    public void Load_CreatesDatabaseFile()
    {
        var service = CreateService();
        service.Load();
        Assert.True(File.Exists(_tempDbPath));
    }

    [Fact]
    public void Load_Twice_DoesNotThrow()
    {
        var service = CreateService();
        service.Load();
        service.Load(); // 应该幂等
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var service = CreateService();
        Assert.True(service.IncludeSubDir);
        Assert.Equal(10, service.WebSearchItemsLimit);
        Assert.Equal(4, service.AutoMatchTagsWebSearchThreadCount);
        Assert.Equal("US", service.ItunesSearchParamsCountry);
        Assert.True(service.LyricDownload_DeleteHeadTag);
        Assert.True(service.LyricDownload_DeleteLinesOfBlankText);
        Assert.True(service.LyricDownload_ReformatTimetag);
        Assert.False(service.LyricDownload_RemoveTimetag);
        Assert.True(service.SaveLrcWhileSaveTags);
        Assert.Equal("utf-8", service.SaveLrcFileDefaultEncoding);
        Assert.Equal("jpg,jpeg,png,bmp,gif", service.PictureFormatLimits);
        Assert.Equal(3000, service.PictureResolutionLimits);
        Assert.Equal(1024, service.PictureSizeLimitsKB);
    }

    [Fact]
    public void SetAndGet_BoolProperty_Works()
    {
        var service = CreateService();
        service.LogEnabled = false;
        Assert.False(service.LogEnabled);

        service.LogEnabled = true;
        Assert.True(service.LogEnabled);
    }

    [Fact]
    public void SetAndGet_StringProperty_Works()
    {
        var service = CreateService();
        service.ItunesSearchParamsCountry = "US";
        Assert.Equal("US", service.ItunesSearchParamsCountry);
    }

    [Fact]
    public void SetAndGet_IntProperty_Works()
    {
        var service = CreateService();
        service.WebSearchItemsLimit = 20;
        Assert.Equal(20, service.WebSearchItemsLimit);
    }

    [Fact]
    public void SetAndGet_NullableStringProperty_Works()
    {
        var service = CreateService();
        service.SaveLrcDirectory = "C:\\Lyrics";
        Assert.Equal("C:\\Lyrics", service.SaveLrcDirectory);

        service.SaveLrcDirectory = null;
        Assert.Null(service.SaveLrcDirectory);
    }

    [Fact]
    public void Save_PersistsToDatabase()
    {
        var service = CreateService();
        service.ItunesSearchParamsCountry = "JP";
        service.WebSearchItemsLimit = 25;
        service.Save();

        // 创建新实例读取同一数据库
        var service2 = CreateService();
        Assert.Equal("JP", service2.ItunesSearchParamsCountry);
        Assert.Equal(25, service2.WebSearchItemsLimit);
    }

    [Fact]
    public void ResetToDefaults_RestoresAllDefaults()
    {
        var service = CreateService();
        service.ItunesSearchParamsCountry = "US";
        service.WebSearchItemsLimit = 99;
        service.ResetToDefaults();

        Assert.Equal("US", service.ItunesSearchParamsCountry);
        Assert.Equal(10, service.WebSearchItemsLimit);
    }

    [Fact]
    public void MultipleProperties_CanBeSetAndRetrieved()
    {
        var service = CreateService();
        service.LyricDownload_DownloadTrans_Enable = true;
        service.LyricDownload_DownloadTrans_ChineseConvMode = "toTraditional";
        service.LyricDownload_DownloadTrans_LyricFormat = "{title}.lrc";
        service.CommentTagWrite163Key = "test_key";
        service.ConnectorsArtists = " & ";

        Assert.True(service.LyricDownload_DownloadTrans_Enable);
        Assert.Equal("toTraditional", service.LyricDownload_DownloadTrans_ChineseConvMode);
        Assert.Equal("{title}.lrc", service.LyricDownload_DownloadTrans_LyricFormat);
        Assert.Equal("test_key", service.CommentTagWrite163Key);
        Assert.Equal(" & ", service.ConnectorsArtists);
    }

    [Fact]
    public void CustomDbPath_IsUsed()
    {
        var customPath = Path.Combine(Path.GetTempPath(), $"custom_{Guid.NewGuid()}.db");
        try
        {
            var service = new SettingsService(customPath);
            service.Load();
            Assert.True(File.Exists(customPath));
        }
        finally
        {
            if (File.Exists(customPath))
                File.Delete(customPath);
        }
    }
}
