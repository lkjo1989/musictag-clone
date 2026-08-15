using MusicTagClone.Interfaces;

namespace MusicTagClone.Tests.Services;

/// <summary>
/// 测试用 IImageCache 直通实现：URL 下载直接调 fetcher 不做缓存；
/// 历史封面写入临时目录。仅用于 CoverService 单元测试注入。
/// </summary>
internal sealed class FakeImageCache : IImageCache
{
    private readonly string _historyDir;

    public FakeImageCache()
    {
        _historyDir = Path.Combine(Path.GetTempPath(), $"faketest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_historyDir);
    }

    public string HistoryDir => _historyDir;
    public string UrlCacheDir => _historyDir;

    public string? StoreHistory(byte[] data)
    {
        if (data == null || data.Length == 0) return null;
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        var name = hash + ".jpg";
        var full = Path.Combine(_historyDir, name);
        if (!File.Exists(full)) File.WriteAllBytes(full, data);
        return name;
    }

    public byte[]? ReadHistory(string relPath)
    {
        var full = GetHistoryFullPath(relPath);
        return File.Exists(full) ? File.ReadAllBytes(full) : null;
    }

    public string GetHistoryFullPath(string relPath) => Path.Combine(_historyDir, Path.GetFileName(relPath));
    public bool HistoryExists(string relPath) => File.Exists(GetHistoryFullPath(relPath));

    public void DeleteHistory(string relPath)
    {
        var full = GetHistoryFullPath(relPath);
        if (File.Exists(full)) File.Delete(full);
    }

    public Task<byte[]?> GetOrDownloadAsync(string url,
        Func<CancellationToken, Task<byte[]?>> fetcher, CancellationToken ct)
        => fetcher(ct);

    public long GetHistorySize() => 0;
    public long GetUrlCacheSize() => 0;
    public void ClearUrlCache() { }
    public void ClearUnreferencedHistory() { }
    public void ClearHistory() { }
    public void Sweep() { }
}
