using Moq;
using MusicTagClone.Interfaces;
using MusicTagClone.Models;
using MusicTagClone.Services;

namespace MusicTagClone.Tests.Services;

public class FileScannerServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<ITagService> _mockTagService;
    private readonly FileScannerService _service;

    public FileScannerServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"musictag_scan_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _mockTagService = new Mock<ITagService>();
        _mockTagService.Setup(s => s.ReadTagsAsync(It.IsAny<string>()))
            .ReturnsAsync(new TagData());
        _service = new FileScannerService(_mockTagService.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string CreateTempFile(string name, string content = "")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Theory]
    [InlineData("test.mp3", true)]
    [InlineData("test.flac", true)]
    [InlineData("test.m4a", true)]
    [InlineData("test.ogg", true)]
    [InlineData("test.wav", true)]
    [InlineData("test.ape", true)]
    [InlineData("test.txt", false)]
    [InlineData("test.jpg", false)]
    [InlineData("test.exe", false)]
    public void IsSupportedFile_ReturnsCorrectResult(string filename, bool expected)
    {
        Assert.Equal(expected, _service.IsSupportedFile(filename));
    }

    [Fact]
    public void GetSupportedExtensions_ContainsCommonFormats()
    {
        var exts = _service.GetSupportedExtensions();
        Assert.Contains(".mp3", exts);
        Assert.Contains(".flac", exts);
        Assert.Contains(".m4a", exts);
        Assert.Contains(".ogg", exts);
        Assert.Contains(".wav", exts);
        Assert.Contains(".ape", exts);
    }

    [Fact]
    public void AddFile_WithSupportedFile_ReturnsMusicFile()
    {
        var path = CreateTempFile("test.mp3");
        var result = _service.AddFile(path);
        Assert.NotNull(result);
        Assert.Equal(path, result!.FilePath);
    }

    [Fact]
    public void AddFile_WithUnsupportedFile_ReturnsNull()
    {
        var path = CreateTempFile("test.txt");
        var result = _service.AddFile(path);
        Assert.Null(result);
    }

    [Fact]
    public void AddFile_WithNonexistentFile_ReturnsNull()
    {
        var result = _service.AddFile("Z:\\nonexistent.mp3");
        Assert.Null(result);
    }

    [Fact]
    public async Task ScanDirectoryAsync_WithMusicFiles_ReturnsAll()
    {
        CreateTempFile("song1.mp3");
        CreateTempFile("song2.flac");
        CreateTempFile("readme.txt"); // 应被忽略

        var results = await _service.ScanDirectoryAsync(_tempDir, false);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task ScanDirectoryAsync_WithSubDirs_IncludesNestedFiles()
    {
        var subDir = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(subDir);
        CreateTempFile("song1.mp3");
        File.WriteAllText(Path.Combine(subDir, "song2.mp3"), "");

        var results = await _service.ScanDirectoryAsync(_tempDir, true);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task ScanDirectoryAsync_WithoutSubDirs_ExcludesNestedFiles()
    {
        var subDir = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(subDir);
        CreateTempFile("song1.mp3");
        File.WriteAllText(Path.Combine(subDir, "song2.mp3"), "");

        var results = await _service.ScanDirectoryAsync(_tempDir, false);
        Assert.Single(results);
    }

    [Fact]
    public async Task ScanDirectoryAsync_WithNonexistentDir_ReturnsEmpty()
    {
        var results = await _service.ScanDirectoryAsync("Z:\\nonexistent", true);
        Assert.Empty(results);
    }

    [Fact]
    public async Task ScanDirectoryAsync_ReportsProgress()
    {
        CreateTempFile("song1.mp3");
        CreateTempFile("song2.mp3");
        CreateTempFile("song3.mp3");

        var progressValues = new List<int>();
        var progress = new Progress<int>(p => progressValues.Add(p));

        await _service.ScanDirectoryAsync(_tempDir, false, progress);
        // 最终进度应等于文件数
        Assert.Contains(3, progressValues);
    }

    [Fact]
    public void FilterFiles_WithKeyword_MatchesTitle()
    {
        var files = new List<MusicFile>
        {
            new() { FilePath = "a.mp3", Title = "Rock Song", Artist = "Band" },
            new() { FilePath = "b.mp3", Title = "Pop Song", Artist = "Singer" },
            new() { FilePath = "c.mp3", Title = "Jazz Track", Artist = "Quartet" },
        };

        var filtered = _service.FilterFiles(files, keyword: "rock");
        Assert.Single(filtered);
        Assert.Equal("a.mp3", filtered[0].FilePath);
    }

    [Fact]
    public void FilterFiles_WithKeyword_MatchesArtist()
    {
        var files = new List<MusicFile>
        {
            new() { FilePath = "a.mp3", Title = "Song A", Artist = "The Beatles" },
            new() { FilePath = "b.mp3", Title = "Song B", Artist = "Queen" },
        };

        var filtered = _service.FilterFiles(files, keyword: "beatles");
        Assert.Single(filtered);
    }

    [Fact]
    public void FilterFiles_WithKeyword_MatchesFileName()
    {
        var files = new List<MusicFile>
        {
            new() { FilePath = "rock_anthem.mp3", Title = "", Artist = "" },
            new() { FilePath = "ballad.mp3", Title = "", Artist = "" },
        };

        var filtered = _service.FilterFiles(files, keyword: "rock");
        Assert.Single(filtered);
    }

    [Fact]
    public void FilterFiles_WithEmptyKeyword_ReturnsAll()
    {
        var files = new List<MusicFile>
        {
            new() { FilePath = "a.mp3" },
            new() { FilePath = "b.mp3" },
        };

        var filtered = _service.FilterFiles(files, keyword: "");
        Assert.Equal(2, filtered.Count);
    }

    [Fact]
    public void SortFiles_ByFileName_Ascending()
    {
        var files = new List<MusicFile>
        {
            new() { FilePath = "c.mp3" },
            new() { FilePath = "a.mp3" },
            new() { FilePath = "b.mp3" },
        };

        var sorted = _service.SortFiles(files, "FileName", true);
        Assert.Equal("a.mp3", sorted[0].FileName);
        Assert.Equal("b.mp3", sorted[1].FileName);
        Assert.Equal("c.mp3", sorted[2].FileName);
    }

    [Fact]
    public void SortFiles_ByFileName_Descending()
    {
        var files = new List<MusicFile>
        {
            new() { FilePath = "a.mp3" },
            new() { FilePath = "c.mp3" },
        };

        var sorted = _service.SortFiles(files, "FileName", false);
        Assert.Equal("c.mp3", sorted[0].FileName);
    }

    [Fact]
    public void SortFiles_ByArtist_SortsCorrectly()
    {
        var files = new List<MusicFile>
        {
            new() { FilePath = "a.mp3", Artist = "Zebra", Title = "T1" },
            new() { FilePath = "b.mp3", Artist = "Apple", Title = "T2" },
        };

        var sorted = _service.SortFiles(files, "Artist", true);
        Assert.Equal("Apple", sorted[0].Artist);
    }

    [Fact]
    public void SortFiles_UnknownField_FallsBackToFileName()
    {
        var files = new List<MusicFile>
        {
            new() { FilePath = "b.mp3" },
            new() { FilePath = "a.mp3" },
        };

        var sorted = _service.SortFiles(files, "UnknownField", true);
        Assert.Equal("a.mp3", sorted[0].FileName);
    }

    [Fact]
    public async Task RenameFilesAsync_RenamesCorrectly()
    {
        var file1 = CreateTempFile("original1.mp3");
        var file2 = CreateTempFile("original2.mp3");

        var musicFiles = new List<MusicFile>
        {
            new() { FilePath = file1, Artist = "A", Title = "T1" },
            new() { FilePath = file2, Artist = "B", Title = "T2" },
        };

        var renamed = await _service.RenameFilesAsync(musicFiles,
            f => $"{f.Artist} - {f.Title}");

        Assert.Equal(2, renamed);
        Assert.True(File.Exists(Path.Combine(_tempDir, "A - T1.mp3")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "B - T2.mp3")));
    }

    [Fact]
    public async Task DeleteFilesAsync_DeletesFiles()
    {
        var file1 = CreateTempFile("delete1.mp3");
        var file2 = CreateTempFile("delete2.mp3");

        var musicFiles = new List<MusicFile>
        {
            new() { FilePath = file1 },
            new() { FilePath = file2 },
        };

        var deleted = await _service.DeleteFilesAsync(musicFiles);
        Assert.Equal(2, deleted);
        Assert.False(File.Exists(file1));
        Assert.False(File.Exists(file2));
    }
}
