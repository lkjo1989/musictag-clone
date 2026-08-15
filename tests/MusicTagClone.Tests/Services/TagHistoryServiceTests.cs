using MusicTagClone.Interfaces;
using MusicTagClone.Models;
using MusicTagClone.Services;

namespace MusicTagClone.Tests.Services;

public class TagHistoryServiceTests
{
    [Fact]
    public void DeletedCover_IsReportedMissing_AndReadReturnsNull()
    {
        var cache = new FakeImageCache();
        var dbPath = Path.Combine(Path.GetTempPath(), $"taghistory_{Guid.NewGuid():N}.db");

        try
        {
            var service = new TagHistoryService(cache, dbPath);
            var filePath = Path.Combine(Path.GetTempPath(), "history-test.mp3");
            var serial = service.TryAddHistory(filePath, new MusicFile
            {
                FilePath = filePath,
                CoverArtData = new byte[] { 1, 2, 3, 4 },
                HasCoverArt = true
            });

            Assert.NotNull(serial);
            var record = Assert.Single(service.GetHistory(filePath));
            Assert.NotNull(record.CoverPath);
            Assert.True(service.CoverExists(record.CoverPath));

            File.Delete(cache.GetHistoryFullPath(record.CoverPath!));

            Assert.False(service.CoverExists(record.CoverPath));
            Assert.Null(service.ReadCoverData(serial!));
        }
        finally
        {
            TryDeleteFile(dbPath);
            TryDeleteFile(dbPath + "-journal");
            TryDeleteDirectory(cache.HistoryDir);
        }
    }

    [Fact]
    public void CacheErrors_DoNotEscapeHistoryService()
    {
        var service = new TagHistoryService(new ThrowingImageCache());

        Assert.False(service.CoverExists("cover.jpg"));
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch { }
    }

    private sealed class ThrowingImageCache : IImageCache
    {
        public string HistoryDir => throw new IOException();
        public string UrlCacheDir => throw new IOException();
        public string? StoreHistory(byte[] data) => throw new IOException();
        public byte[]? ReadHistory(string relPath) => throw new IOException();
        public string GetHistoryFullPath(string relPath) => throw new IOException();
        public bool HistoryExists(string relPath) => throw new IOException();
        public void DeleteHistory(string relPath) => throw new IOException();
        public Task<byte[]?> GetOrDownloadAsync(string url,
            Func<CancellationToken, Task<byte[]?>> fetcher, CancellationToken ct) => throw new IOException();
        public long GetHistorySize() => throw new IOException();
        public long GetUrlCacheSize() => throw new IOException();
        public void ClearUrlCache() => throw new IOException();
        public void ClearUnreferencedHistory() => throw new IOException();
        public void ClearHistory() => throw new IOException();
        public void Sweep() => throw new IOException();
    }
}
