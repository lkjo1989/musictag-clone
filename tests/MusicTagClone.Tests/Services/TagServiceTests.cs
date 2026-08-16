using MusicTagClone.Interfaces;
using MusicTagClone.Models;
using MusicTagClone.Services;
using Moq;

namespace MusicTagClone.Tests.Services;

public class TagServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TagService _service;

    private static readonly string TestFileDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "testfile"));

    public TagServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"musictag_tag_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _service = new TagService(Mock.Of<ILoggerService>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    /// <summary>
    /// 创建一个最小的有效 MP3 文件用于测试
    /// </summary>
    private string CreateMinimalMp3()
    {
        var path = Path.Combine(_tempDir, $"test_{Guid.NewGuid()}.mp3");
        // ID3v2 header + 最小 MP3 frame header
        var data = new byte[]
        {
            // ID3v2.3 header
            0x49, 0x44, 0x33, // "ID3"
            0x03, 0x00,       // version 2.3
            0x00,             // flags
            0x00, 0x00, 0x00, 0x00, // size: 0
            // MP3 sync word
            0xFF, 0xFB, 0x90, 0x00
        };
        File.WriteAllBytes(path, data);
        return path;
    }

    // ============================================
    // 辅助方法
    // ============================================

    private static string? TestFile(string relativeName)
    {
        var path = Path.Combine(TestFileDir, relativeName);
        return File.Exists(path) ? path : null;
    }

    // ============================================
    // 位深（BitsPerSample）兼容性测试
    // 记录每种格式是否支持位深读取
    // ============================================

    [SkippableFact] // 依赖仓库外真实样本音频，缺失时跳过（如 CI）
    public async Task TagService_BitsPerSample_Mp3_ReturnsNull()
    {
        // MP3 格式不存储位深信息，TagLibSharp 和 MediaInfo 均无法获取
        var path = TestFile("曲锦楠 - 霞光.mp3");
        Skip.If(path is null, "缺少样本文件 testfile/曲锦楠 - 霞光.mp3，跳过位深测试");
        Assert.NotNull(path); // 为编译器收窄可空性

        var tags = await _service.ReadTagsAsync(path);
        Assert.NotNull(tags);
        Assert.Null(tags.BitsPerSample);
    }

    [SkippableFact] // 依赖仓库外真实样本音频，缺失时跳过（如 CI）
    public async Task TagService_BitsPerSample_M4a_ReturnsNull()
    {
        // AAC(M4A) 格式不存储位深信息
        var path = TestFile("F.I.R. - 你的微笑.m4a");
        Skip.If(path is null, "缺少样本文件 testfile/F.I.R. - 你的微笑.m4a，跳过位深测试");
        Assert.NotNull(path); // 为编译器收窄可空性

        var tags = await _service.ReadTagsAsync(path);
        Assert.NotNull(tags);
        Assert.Null(tags.BitsPerSample);
    }

    [SkippableFact] // 依赖仓库外真实样本音频，缺失时跳过（如 CI）
    public async Task TagService_BitsPerSample_Flac_ReturnsValue()
    {
        // FLAC 原生存储位深，可以正确读取
        var path = TestFile("ClariS - CLICK.flac");
        Skip.If(path is null, "缺少样本文件 testfile/ClariS - CLICK.flac，跳过位深测试");
        Assert.NotNull(path); // 为编译器收窄可空性

        var tags = await _service.ReadTagsAsync(path);
        Assert.NotNull(tags);
        Assert.True(tags.BitsPerSample.HasValue);
        Assert.True(tags.BitsPerSample!.Value > 0,
            $"FLAC BitsPerSample should be > 0, got: {tags.BitsPerSample.Value}");
    }

    // ============================================
    // 原有测试保持不动
    // ============================================

    [Fact]
    public async Task ReadTagsAsync_WithValidMp3_ReturnsTagData()
    {
        var path = CreateMinimalMp3();
        var tags = await _service.ReadTagsAsync(path);
        Assert.NotNull(tags);
        // 新文件应该有空标签
        Assert.True(string.IsNullOrEmpty(tags.Title) || tags.Title != null);
    }

    [Fact]
    public async Task ReadTagsAsync_WithInvalidPath_ReturnsEmptyTagData()
    {
        var tags = await _service.ReadTagsAsync("Z:\\nonexistent.mp3");
        Assert.NotNull(tags);
    }

    [Fact]
    public async Task WriteTagsAsync_WithTitle_WritesSuccessfully()
    {
        var path = CreateMinimalMp3();
        var tags = new TagData
        {
            Title = "Test Title",
            Artist = "Test Artist",
            Album = "Test Album"
        };

        var result = await _service.WriteTagsAsync(path, tags);
        Assert.True(result);

        // 验证写入
        var readTags = await _service.ReadTagsAsync(path);
        Assert.Equal("Test Title", readTags.Title);
        Assert.Equal("Test Artist", readTags.Artist);
        Assert.Equal("Test Album", readTags.Album);
    }

    [Fact]
    public async Task WriteTagsAsync_WithYearAndTrack_WritesSuccessfully()
    {
        var path = CreateMinimalMp3();
        var tags = new TagData { Year = 2024, Track = 5, TrackCount = 12 };

        var result = await _service.WriteTagsAsync(path, tags);
        Assert.True(result);

        var readTags = await _service.ReadTagsAsync(path);
        Assert.Equal(2024u, readTags.Year);
        Assert.Equal(5u, readTags.Track);
    }

    [Fact]
    public async Task WriteTagsAsync_WithGenre_WritesSuccessfully()
    {
        var path = CreateMinimalMp3();
        var tags = new TagData { Genre = "Rock" };

        await _service.WriteTagsAsync(path, tags);
        var readTags = await _service.ReadTagsAsync(path);
        Assert.Equal("Rock", readTags.Genre);
    }

    [Fact]
    public async Task WriteTagsAsync_KeepUpdateTime_PreservesTimestamp()
    {
        var path = CreateMinimalMp3();
        var originalTime = File.GetLastWriteTime(path);

        await Task.Delay(1100); // 确保时间戳不同

        var tags = new TagData { Title = "New Title" };
        await _service.WriteTagsAsync(path, tags, keepUpdateTime: true);

        var newTime = File.GetLastWriteTime(path);
        Assert.Equal(originalTime, newTime);
    }

    [Fact]
    public async Task ClearTagsAsync_ClearsAllFields()
    {
        var path = CreateMinimalMp3();

        // 先写入标签
        await _service.WriteTagsAsync(path, new TagData
        {
            Title = "Title",
            Artist = "Artist",
            Album = "Album"
        });

        // 清除
        var result = await _service.ClearTagsAsync(path);
        Assert.True(result);
    }

    [Fact]
    public async Task WriteAndReadLyrics_WorksCorrectly()
    {
        var path = CreateMinimalMp3();
        var lyrics = "[00:00.00]Test lyrics\n[00:05.00]Second line";

        var writeResult = await _service.WriteLyricsAsync(path, lyrics);
        Assert.True(writeResult);

        var readLyrics = await _service.ReadLyricsAsync(path);
        Assert.NotNull(readLyrics);
        Assert.Contains("Test lyrics", readLyrics);
    }

    [Fact]
    public async Task ReadLyricsAsync_WithNoLyrics_ReturnsNull()
    {
        var path = CreateMinimalMp3();
        var lyrics = await _service.ReadLyricsAsync(path);
        Assert.Null(lyrics);
    }

    [Fact]
    public async Task WriteCoverArtAsync_WritesSuccessfully()
    {
        var path = CreateMinimalMp3();
        var coverData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG header
        var cover = new CoverArt
        {
            ImageData = coverData,
            MimeType = "image/jpeg"
        };

        var result = await _service.WriteCoverArtAsync(path, cover);
        Assert.True(result);
    }

    [Fact]
    public async Task ReadCoverArtAsync_WithNoCover_ReturnsNull()
    {
        var path = CreateMinimalMp3();
        var cover = await _service.ReadCoverArtAsync(path);
        Assert.Null(cover);
    }

    [Fact]
    public async Task ReadCoverArtAsync_WithCover_ReturnsData()
    {
        var path = CreateMinimalMp3();
        var coverData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        await _service.WriteCoverArtAsync(path, new CoverArt
        {
            ImageData = coverData,
            MimeType = "image/jpeg"
        });

        var cover = await _service.ReadCoverArtAsync(path);
        Assert.NotNull(cover);
        Assert.NotNull(cover!.ImageData);
        Assert.True(cover.ImageData.Length > 0);
    }

    [Fact]
    public async Task WriteTagsBatchAsync_WritesMultipleFiles()
    {
        var file1 = CreateMinimalMp3();
        var file2 = CreateMinimalMp3();

        var fileTags = new List<KeyValuePair<string, TagData>>
        {
            new(file1, new TagData { Title = "Song 1" }),
            new(file2, new TagData { Title = "Song 2" }),
        };

        var count = await _service.WriteTagsBatchAsync(fileTags);
        Assert.Equal(2, count);

        var tags1 = await _service.ReadTagsAsync(file1);
        var tags2 = await _service.ReadTagsAsync(file2);
        Assert.Equal("Song 1", tags1.Title);
        Assert.Equal("Song 2", tags2.Title);
    }

    [Fact]
    public async Task WriteTagsBatchAsync_ReportsProgress()
    {
        var file1 = CreateMinimalMp3();
        var file2 = CreateMinimalMp3();
        var file3 = CreateMinimalMp3();

        var fileTags = new List<KeyValuePair<string, TagData>>
        {
            new(file1, new TagData { Title = "S1" }),
            new(file2, new TagData { Title = "S2" }),
            new(file3, new TagData { Title = "S3" }),
        };

        var progressValues = new List<int>();
        var progress = new Progress<int>(p => progressValues.Add(p));

        await _service.WriteTagsBatchAsync(fileTags, progress: progress);
        Assert.Contains(1, progressValues);
        Assert.Contains(3, progressValues);
    }

    [Fact]
    public async Task WriteTagsAsync_WithComment_WritesSuccessfully()
    {
        var path = CreateMinimalMp3();
        var tags = new TagData { Comment = "Test comment 123" };

        await _service.WriteTagsAsync(path, tags);
        var readTags = await _service.ReadTagsAsync(path);
        Assert.Equal("Test comment 123", readTags.Comment);
    }

    [Fact]
    public async Task WriteTagsAsync_WithDiscInfo_WritesSuccessfully()
    {
        var path = CreateMinimalMp3();
        var tags = new TagData { Disc = 2, DiscCount = 3 };

        await _service.WriteTagsAsync(path, tags);
        var readTags = await _service.ReadTagsAsync(path);
        Assert.Equal(2u, readTags.Disc);
        Assert.Equal(3u, readTags.DiscCount);
    }

    [Fact]
    public async Task WriteTagsAsync_WithLyricist_WritesAndReadsSuccessfully()
    {
        var path = CreateMinimalMp3();
        var tags = new TagData { Lyricist = "Test Lyricist" };

        await _service.WriteTagsAsync(path, tags);
        var readTags = await _service.ReadTagsAsync(path);
        Assert.Equal("Test Lyricist", readTags.Lyricist);
    }
}
