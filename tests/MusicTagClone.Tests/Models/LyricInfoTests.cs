using MusicTagClone.Models;

namespace MusicTagClone.Tests.Models;

public class LyricInfoTests
{
    [Fact]
    public void HasTranslation_WithNull_ReturnsFalse()
    {
        var info = new LyricInfo { TranslatedLyric = null };
        Assert.False(info.HasTranslation);
    }

    [Fact]
    public void HasTranslation_WithEmpty_ReturnsFalse()
    {
        var info = new LyricInfo { TranslatedLyric = "" };
        Assert.False(info.HasTranslation);
    }

    [Fact]
    public void HasTranslation_WithContent_ReturnsTrue()
    {
        var info = new LyricInfo { TranslatedLyric = "翻译歌词" };
        Assert.True(info.HasTranslation);
    }

    [Fact]
    public void DownloadConfig_HasCorrectDefaults()
    {
        var config = new LyricInfo.DownloadConfig();
        Assert.False(config.DownloadTranslation);
        Assert.False(config.DontDownloadOriginal);
        Assert.True(config.ReformatTimetag);
        Assert.False(config.RemoveTimetag);
        Assert.False(config.DeleteHeadTag);
        Assert.True(config.DeleteBlankLines);
        Assert.Equal("none", config.ChineseConvMode);
    }

    [Fact]
    public void SaveConfig_HasCorrectDefaults()
    {
        var config = new LyricInfo.SaveConfig();
        Assert.Equal(string.Empty, config.SaveDirectory);
        Assert.Equal("utf-8", config.FileDefaultEncoding);
        Assert.Equal("{artist} - {title}.lrc", config.FilenameFormat);
        Assert.True(config.SaveLrcWhileSaveTags);
    }
}
