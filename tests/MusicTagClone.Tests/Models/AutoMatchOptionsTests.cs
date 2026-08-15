using MusicTagClone.Models;
using MusicTagClone.Services;

namespace MusicTagClone.Tests.Models;

public sealed class AutoMatchOptionsTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), "automatch_" + Guid.NewGuid() + ".db");
    public void Dispose() { if (File.Exists(_db)) File.Delete(_db); }

    [Fact]
    public void Defaults_MatchOriginal()
    {
        var settings = new SettingsService(_db);
        var options = AutoMatchOptions.Load(settings);
        Assert.True(options.Get(AutoMatchOptions.Cover).Enabled);
        Assert.True(options.Get(AutoMatchOptions.Lyrics).Enabled);
        Assert.False(options.Get(AutoMatchOptions.Title).Enabled);
        Assert.Equal(4, options.ThreadCount);
    }

    [Fact]
    public void SaveLoad_RoundTripsAndUsesOriginalTupleShape()
    {
        var settings = new SettingsService(_db);
        var options = new AutoMatchOptions();
        options.Get(AutoMatchOptions.Lyrics).Enabled = false;
        options.Get(AutoMatchOptions.Title).Enabled = true;
        options.Get(AutoMatchOptions.Title).Overwrite = true;
        options.Get(AutoMatchOptions.Cover).WriteMode = AutoMatchWriteMode.SaveToTagAndFile;
        options.ThreadCount = 16;
        options.Save(settings);

        var loaded = AutoMatchOptions.Load(new SettingsService(_db));
        Assert.True(loaded.Get(AutoMatchOptions.Title).Enabled);
        Assert.True(loaded.Get(AutoMatchOptions.Title).Overwrite);
        Assert.Equal(AutoMatchWriteMode.SaveToTagAndFile, loaded.Get(AutoMatchOptions.Cover).WriteMode);
        Assert.Equal(16, loaded.ThreadCount);
        Assert.False(loaded.Get(AutoMatchOptions.Lyrics).Enabled);
    }

    [Fact]
    public void ThreadCount_IsClampedToSixteen()
    {
        var settings = new SettingsService(_db);
        settings.AutoMatchTagsWebSearchThreadCount = 20;
        settings.Save();

        var loaded = AutoMatchOptions.Load(new SettingsService(_db));
        Assert.Equal(16, loaded.ThreadCount);

        loaded.ThreadCount = 20;
        loaded.Save(settings);
        Assert.Equal(16, settings.AutoMatchTagsWebSearchThreadCount);
    }

    [Theory]
    [InlineData("instrumental", true)]
    [InlineData("Live (Off Vocal)", true)]
    [InlineData("伴奏版", true)]
    [InlineData("普通歌曲", false)]
    public void InstrumentalTitle_IsDetected(string title, bool expected)
    {
        Assert.Equal(expected, AutoMatchOptions.IsInstrumentalTitle(title));
    }
}
