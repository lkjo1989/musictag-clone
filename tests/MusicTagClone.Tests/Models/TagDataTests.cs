using MusicTagClone.Models;

namespace MusicTagClone.Tests.Models;

public class TagDataTests
{
    [Fact]
    public void FromMusicFile_CopiesAllFields()
    {
        var file = new MusicFile
        {
            Title = "Test Title",
            Artist = "Test Artist",
            Album = "Test Album",
            Year = 2024,
            Track = 5,
            TrackCount = 12,
            Genre = "Rock",
            Comment = "Test Comment",
            AlbumArtist = "Album Artist",
            Composer = "Composer",
            Disc = 1,
            DiscCount = 2,
            Lyrics = "[00:00.00]Test lyrics"
        };

        var tag = TagData.FromMusicFile(file);

        Assert.Equal("Test Title", tag.Title);
        Assert.Equal("Test Artist", tag.Artist);
        Assert.Equal("Test Album", tag.Album);
        Assert.Equal(2024u, tag.Year);
        Assert.Equal(5u, tag.Track);
        Assert.Equal(12u, tag.TrackCount);
        Assert.Equal("Rock", tag.Genre);
        Assert.Equal("Test Comment", tag.Comment);
        Assert.Equal("Album Artist", tag.AlbumArtist);
        Assert.Equal("Composer", tag.Composer);
        Assert.Equal(1u, tag.Disc);
        Assert.Equal(2u, tag.DiscCount);
        Assert.Equal("[00:00.00]Test lyrics", tag.Lyrics);
    }

    [Fact]
    public void FromMusicFile_WithEmptyLyrics_SetsNull()
    {
        var file = new MusicFile { Lyrics = "" };
        var tag = TagData.FromMusicFile(file);
        Assert.Null(tag.Lyrics);
    }

    [Fact]
    public void HasAnyValue_WithAllNull_ReturnsFalse()
    {
        var tag = new TagData();
        Assert.False(tag.HasAnyValue);
    }

    [Theory]
    [InlineData("Title", null, null, null, null, null, null, null, null, null, null, null, null, null)]
    [InlineData(null, "Artist", null, null, null, null, null, null, null, null, null, null, null, null)]
    [InlineData(null, null, null, null, null, null, "Genre", null, null, null, null, null, null, null)]
    [InlineData(null, null, null, null, null, null, null, null, null, null, null, null, null, "lyrics")]
    public void HasAnyValue_WithOneFieldSet_ReturnsTrue(
        string? title, string? artist, string? album, uint? year, uint? track, uint? trackCount,
        string? genre, string? comment, string? albumArtist, string? composer,
        uint? disc, uint? discCount, byte[]? cover, string? lyrics)
    {
        var tag = new TagData
        {
            Title = title, Artist = artist, Album = album, Year = year, Track = track,
            TrackCount = trackCount, Genre = genre, Comment = comment,
            AlbumArtist = albumArtist, Composer = composer, Disc = disc, DiscCount = discCount,
            CoverArtData = cover, Lyrics = lyrics
        };
        Assert.True(tag.HasAnyValue);
    }
}
