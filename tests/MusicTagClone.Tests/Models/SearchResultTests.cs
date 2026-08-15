using MusicTagClone.Models;

namespace MusicTagClone.Tests.Models;

public class SearchResultTests
{
    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        var result = new SearchResult
        {
            SourceName = "TestSource",
            Artist = "Artist",
            Title = "Title",
            MatchScore = 0.85
        };
        Assert.Contains("TestSource", result.ToString());
        Assert.Contains("Artist", result.ToString());
        Assert.Contains("Title", result.ToString());
    }

    [Fact]
    public void GetIdentityKey_忽略封面缓存路径并保留结果字段()
    {
        var first = new SearchResult
        {
            SourceName = "iTunes",
            SourceUrl = "https://example.test/1",
            Title = "Title",
            Artist = "Artist",
            Album = "Album",
            CoverTempPath = "cache/history/a.jpg",
            ExtraFields = new Dictionary<string, string> { ["track"] = "1" },
        };
        var second = new SearchResult
        {
            SourceName = first.SourceName,
            SourceUrl = first.SourceUrl,
            Title = first.Title,
            Artist = first.Artist,
            Album = first.Album,
            CoverTempPath = "cache/history/b.jpg",
            ExtraFields = new Dictionary<string, string> { ["track"] = "1" },
        };

        Assert.Equal(first.GetIdentityKey(), second.GetIdentityKey());
        second.ExtraFields["track"] = "2";
        Assert.NotEqual(first.GetIdentityKey(), second.GetIdentityKey());
    }
}

public class SearchConditionTests
{
    [Fact]
    public void BuildSearchQuery_WithCustomQuery_ReturnsCustomQuery()
    {
        var file = new MusicFile { Artist = "Artist", Title = "Title", Album = "Album" };
        var condition = new SearchCondition { CustomQuery = "custom search" };
        Assert.Equal("custom search", condition.BuildSearchQuery(file));
    }

    [Fact]
    public void BuildSearchQuery_UseOnlyFilename_ReturnsFilename()
    {
        var file = new MusicFile
        {
            FilePath = "C:\\music\\test.mp3",
            Artist = "Artist",
            Title = "Title"
        };
        var condition = new SearchCondition { UseOnlyFilename = true };
        Assert.Equal("Artist test", condition.BuildSearchQuery(file));
    }

    [Fact]
    public void BuildSearchQuery_UsesSelectedFieldsInConfiguredOrder()
    {
        var file = new MusicFile
        {
            FilePath = "C:\\music\\filename.mp3",
            Title = "Title",
            Artist = "Artist",
            Album = "Album"
        };
        var condition = new SearchCondition
        {
            UseTitle = true,
            UseArtist = true,
            UseAlbum = true
        };

        Assert.Equal("Title Artist Album", condition.BuildSearchQuery(file));
    }

    [Fact]
    public void BuildSearchQuery_ExcludesUnselectedFields()
    {
        var file = new MusicFile
        {
            FilePath = "C:\\music\\filename.mp3",
            Title = "Title",
            Artist = "Artist",
            Album = "Album"
        };
        var condition = new SearchCondition
        {
            UseTitle = false,
            UseArtist = true,
            UseAlbum = false
        };

        Assert.Equal("Artist", condition.BuildSearchQuery(file));
    }

    [Fact]
    public void BuildSearchQuery_UsesConfiguredFieldOrder()
    {
        var file = new MusicFile { Title = "Title", Artist = "Artist", Album = "Album" };
        var condition = new SearchCondition
        {
            UseTitle = true,
            UseArtist = true,
            UseAlbum = true,
            FieldOrder = new List<string>
            {
                SearchCondition.AlbumKey,
                SearchCondition.ArtistKey,
                SearchCondition.TitleKey
            }
        };

        Assert.Equal("Album Artist Title", condition.BuildSearchQuery(file));
    }

    [Fact]
    public void SearchConditionCatalog_PreservesEnabledStateAndOrder()
    {
        var json = SearchConditionCatalog.Serialize(new List<SearchConditionItem>
        {
            new() { Key = SearchCondition.AlbumKey, Label = "专辑", Enabled = true },
            new() { Key = SearchCondition.TitleKey, Label = "标题", Enabled = false },
            new() { Key = SearchCondition.ArtistKey, Label = "艺术家", Enabled = true }
        });

        var items = SearchConditionCatalog.Load(json, true, true, false);

        Assert.Equal(
            new[] { SearchCondition.AlbumKey, SearchCondition.TitleKey, SearchCondition.ArtistKey },
            items.Select(item => item.Key));
        Assert.False(items[1].Enabled);
        Assert.Equal(new[] { SearchCondition.AlbumKey, SearchCondition.ArtistKey },
            SearchConditionCatalog.GetEnabledKeys(json, true, true, false));
    }

    [Fact]
    public void BuildSearchQuery_EmptyTitle_UsesCompleteFilenameAsTitle()
    {
        var file = new MusicFile
        {
            FilePath = "C:\\music\\Artist - Title.flac",
            Title = "",
            Artist = "",
            Album = ""
        };
        var condition = new SearchCondition { UseTitle = false, UseArtist = false, UseAlbum = false };

        Assert.Equal("Artist - Title", condition.BuildSearchQuery(file));
    }

    [Fact]
    public void BuildSearchQuery_WithArtistAndTitle_IncludesBoth()
    {
        var file = new MusicFile { Artist = "Artist", Title = "Title" };
        var condition = new SearchCondition { UseArtist = true, UseAlbum = false };
        var query = condition.BuildSearchQuery(file);
        Assert.Contains("Artist", query);
        Assert.Contains("Title", query);
    }

    [Fact]
    public void BuildSearchQuery_WithAlbum_IncludesAlbum()
    {
        var file = new MusicFile { Artist = "A", Title = "T", Album = "MyAlbum" };
        var condition = new SearchCondition { UseArtist = true, UseAlbum = true };
        var query = condition.BuildSearchQuery(file);
        Assert.Contains("MyAlbum", query);
    }

    [Fact]
    public void BuildSearchQuery_WithoutAlbum_ExcludesAlbum()
    {
        var file = new MusicFile { Artist = "A", Title = "T", Album = "MyAlbum" };
        var condition = new SearchCondition { UseArtist = true, UseAlbum = false };
        var query = condition.BuildSearchQuery(file);
        Assert.DoesNotContain("MyAlbum", query);
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var condition = new SearchCondition();
        Assert.True(condition.UseTitle);
        Assert.True(condition.UseArtist);
        Assert.False(condition.UseAlbum);
        Assert.False(condition.UseOnlyFilename);
        Assert.Equal("US", condition.ItunesCountry);
        Assert.Equal(10, condition.WebSearchItemsLimit);
        Assert.Equal(0, condition.WebSearchItemsOffset);
        Assert.Equal(4, condition.WebSearchThreadCount);
    }
}
