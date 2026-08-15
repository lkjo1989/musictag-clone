using MusicTagClone.Models;

namespace MusicTagClone.Tests.Models;

public class MusicFileTests
{
    [Fact]
    public void FromPath_WithExistingFile_SetsProperties()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "test content");
            var mf = MusicFile.FromPath(tempFile);

            Assert.Equal(tempFile, mf.FilePath);
            Assert.Equal(Path.GetFileName(tempFile), mf.FileName);
            Assert.Equal(Path.GetDirectoryName(tempFile), mf.Directory);
            Assert.Equal(Path.GetExtension(tempFile).ToLowerInvariant(), mf.Extension);
            Assert.True(mf.FileSize > 0);
            Assert.True(mf.LastModified > DateTime.MinValue);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void FromPath_WithNonexistentFile_SetsZeroSize()
    {
        var mf = MusicFile.FromPath("Z:\\nonexistent\\file.mp3");

        Assert.Equal("file.mp3", mf.FileName);
        Assert.Equal(0, mf.FileSize);
    }

    [Fact]
    public void ToString_WithArtistAndTitle_ReturnsFormatted()
    {
        var mf = new MusicFile { Artist = "TestArtist", Title = "TestTitle" };
        Assert.Equal("TestArtist - TestTitle", mf.ToString());
    }

    [Fact]
    public void ToString_WithEmptyFields_ReturnsTrimmed()
    {
        var mf = new MusicFile { Artist = "", Title = "" };
        Assert.Equal("", mf.ToString());
    }

    [Fact]
    public void ToString_WithOnlyTitle_ReturnsTitle()
    {
        var mf = new MusicFile { Title = "MySong" };
        Assert.Equal("MySong", mf.ToString());
    }

    [Theory]
    [InlineData("test.mp3", ".mp3")]
    [InlineData("test.FLAC", ".flac")]
    [InlineData("test.m4a", ".m4a")]
    public void Extension_ReturnsLowercase(string filename, string expectedExt)
    {
        var mf = new MusicFile { FilePath = $"C:\\{filename}" };
        Assert.Equal(expectedExt, mf.Extension);
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var mf = new MusicFile();

        Assert.Equal(string.Empty, mf.FilePath);
        Assert.Equal(string.Empty, mf.Title);
        Assert.Equal(string.Empty, mf.Artist);
        Assert.Equal(string.Empty, mf.Album);
        Assert.Null(mf.Year);
        Assert.Null(mf.Track);
        Assert.False(mf.HasCoverArt);
        Assert.False(mf.HasLyrics);
        Assert.False(mf.IsModified);
        Assert.False(mf.IsSelected);
        Assert.False(mf.IsChecked);
    }
}
