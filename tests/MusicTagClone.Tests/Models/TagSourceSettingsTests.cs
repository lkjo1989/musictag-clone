using MusicTagClone.Models;

namespace MusicTagClone.Tests.Models;

public sealed class TagSourceSettingsTests
{
    [Fact]
    public void Defaults_MatchOriginalSourceGroups()
    {
        var lyrics = TagSourceCatalog.Load(null, TagSourceCategory.Lyrics);
        var pictures = TagSourceCatalog.Load(null, TagSourceCategory.Picture);
        var combTags = TagSourceCatalog.Load(null, TagSourceCategory.CombinationTags);

        Assert.Equal(new[] { "netease", "qq", "kugou", "kuwo" }, lyrics.Select(s => s.Key));
        Assert.Equal(new[] { "netease", "qq", "itunes", "kuwo", "lastfm", "musicbrainz", "discogs" },
            pictures.Select(s => s.Key));
        Assert.Equal(new[] { "netease", "qq", "itunes", "kuwo", "lastfm", "musicbrainz", "discogs" },
            combTags.Select(s => s.Key));
        Assert.Equal(new[] { true, true, true, false }, lyrics.Select(s => s.Enabled));
        Assert.Equal(new[] { true, true, false, false, false, false, false }, pictures.Select(s => s.Enabled));
        Assert.Equal(new[] { true, true, false, false, false, false, false }, combTags.Select(s => s.Enabled));
    }

    [Fact]
    public void SaveLoad_PreservesEnabledStateAndOrder()
    {
        var sources = TagSourceCatalog.Load(null, TagSourceCategory.Lyrics);
        var first = sources[0];
        sources.RemoveAt(0);
        sources.Add(first);
        sources[0].Enabled = false;

        var loaded = TagSourceCatalog.Load(TagSourceCatalog.Serialize(sources), TagSourceCategory.Lyrics);

        Assert.Equal("qq", loaded[0].Key);
        Assert.False(loaded[0].Enabled);
        Assert.Equal("netease", loaded[loaded.Count - 1].Key);
        Assert.True(loaded[loaded.Count - 1].Enabled);
    }

    [Fact]
    public void Load_AcceptsOriginalNumericSourceIds()
    {
        var json = "[{\"Src\":0,\"Enabled\":false,\"Seq\":3},{\"Src\":3,\"Enabled\":true,\"Seq\":0}]";

        var loaded = TagSourceCatalog.Load(json, TagSourceCategory.Lyrics);

        Assert.Equal("kugou", loaded[0].Key);
        Assert.True(loaded[0].Enabled);
        Assert.False(loaded.Single(s => s.Key == "netease").Enabled);
    }
}
