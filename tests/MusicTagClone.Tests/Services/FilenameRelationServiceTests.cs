using MusicTagClone.Interfaces;
using MusicTagClone.Models;
using MusicTagClone.Services;
using Moq;

namespace MusicTagClone.Tests.Services;

public class FilenameRelationServiceTests
{
    [Theory]
    [InlineData("@2 - @1")]
    [InlineData("@5. @2 - @1")]
    [InlineData("@4@5. @2 - @1")]
    public void ValidatePattern_OriginalPresets_AreValid(string pattern)
    {
        Assert.True(FilenameRelationService.TryValidatePattern(pattern, true, out var error), error);
    }

    [Fact]
    public void ValidatePattern_DuplicateField_IsRejected()
    {
        Assert.False(FilenameRelationService.TryValidatePattern("@1 - @1", true, out _));
    }

    [Fact]
    public void BuildFilename_UsesOriginalTrackFormatting()
    {
        var file = new MusicFile
        {
            Artist = "Artist", Title = "Title", Disc = 2, Track = 3,
        };

        Assert.Equal("203. Artist - Title",
            FilenameRelationService.BuildFilename(file, "@4@5. @2 - @1"));
    }

    [Fact]
    public void ParsePattern_ArtistDashTitle_LoadsBothTags()
    {
        var options = new FilenameRelationOptions
        {
            Mode = FilenameRelationMode.ChangeTags,
            Pattern = "@2 - @1",
        };

        var changed = FilenameRelationService.TryParseFilename(
            "C:\\music\\Artist - Title.flac", options, new TagData(), out var tags);

        Assert.True(changed);
        Assert.Equal("Artist", tags.Artist);
        Assert.Equal("Title", tags.Title);
    }

    [Fact]
    public void ParsePattern_DiscTrackPrefix_SplitsCombinedNumber()
    {
        var options = new FilenameRelationOptions
        {
            Mode = FilenameRelationMode.ChangeTags,
            Pattern = "@4@5. @2 - @1",
        };

        FilenameRelationService.TryParseFilename(
            "C:\\music\\203. Artist - Title.flac", options, new TagData(), out var tags);

        Assert.Equal((uint)2, tags.Disc);
        Assert.Equal((uint)3, tags.Track);
        Assert.Equal("Artist", tags.Artist);
        Assert.Equal("Title", tags.Title);
    }

    [Fact]
    public void ParseRegex_UsesConfiguredCaptureGroups()
    {
        var options = new FilenameRelationOptions
        {
            Mode = FilenameRelationMode.ChangeTags,
            UseRegex = true,
            RegexPattern = "^(.*?) - (.*?)$",
            RegexGroupMap = new Dictionary<int, int> { [1] = 2, [2] = 1 },
        };

        FilenameRelationService.TryParseFilename(
            "C:\\music\\Artist - Title.mp3", options, new TagData(), out var tags);

        Assert.Equal("Artist", tags.Artist);
        Assert.Equal("Title", tags.Title);
    }

    [Fact]
    public async Task Rename_RenamesSidecarsAndAddsCollisionSuffix()
    {
        var root = Path.Combine(Path.GetTempPath(), "musictag-filename-rel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "old.mp3");
            var lrc = Path.Combine(root, "old.lrc");
            var image = Path.Combine(root, "old.jpg");
            File.WriteAllText(source, "audio");
            File.WriteAllText(lrc, "lyric");
            File.WriteAllText(image, "image");
            File.WriteAllText(Path.Combine(root, "Artist - Title.mp3"), "collision");

            var tags = new Mock<ITagService>();
            tags.Setup(service => service.ReadTagsAsync(source)).ReturnsAsync(new TagData
            {
                Artist = "Artist", Title = "Title",
            });
            var settings = new Mock<ISettingsService>();
            var service = new FilenameRelationService(tags.Object, settings.Object);
            var file = MusicFile.FromPath(source);

            var result = await service.ExecuteAsync(new[] { file }, new FilenameRelationOptions
            {
                Pattern = "@2 - @1",
                Mode = FilenameRelationMode.RenameFiles,
                RenameRelatedFiles = true,
            }, false);

            Assert.Equal(1, result.ChangedCount);
            Assert.Equal("Artist - Title (1).mp3", Path.GetFileName(file.FilePath));
            Assert.True(File.Exists(Path.Combine(root, "Artist - Title (1).lrc")));
            Assert.True(File.Exists(Path.Combine(root, "Artist - Title (1).jpg")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
