using MusicTagClone.Interfaces;
using MusicTagClone.Models;

namespace MusicTagClone.Services;

/// <summary>
/// 文件扫描/操作服务
/// </summary>
public class FileScannerService : IFileScannerService
{
    private static readonly HashSet<string> DefaultExtensions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".mp4", ".ogg", ".wma", ".wav",
        ".aiff", ".aif", ".ape", ".wv", ".mpc", ".opus"
    };

    private readonly ITagService _tagService;

    public FileScannerService(ITagService tagService)
    {
        _tagService = tagService;
    }

    public async Task<IReadOnlyList<MusicFile>> ScanDirectoryAsync(
        string directory, bool includeSubDirs = true, IProgress<int>? progress = null)
    {
        if (!Directory.Exists(directory))
            return Array.Empty<MusicFile>();

        var option = includeSubDirs ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var allFiles = Directory.GetFiles(directory, "*.*", option);
        var musicFiles = allFiles.Where(IsSupportedFile).ToList();

        var results = new List<MusicFile>(musicFiles.Count);
        for (var i = 0; i < musicFiles.Count; i++)
        {
            var mf = MusicFile.FromPath(musicFiles[i]);
            var tags = await _tagService.ReadTagsAsync(musicFiles[i]);
            ApplyTags(mf, tags);
            results.Add(mf);
            progress?.Report(i + 1);
        }

        return results;
    }

    public MusicFile? AddFile(string filePath)
    {
        if (!File.Exists(filePath) || !IsSupportedFile(filePath))
            return null;
        return MusicFile.FromPath(filePath);
    }

    public IReadOnlyList<MusicFile> FilterFiles(
        IEnumerable<MusicFile> files, string? keyword = null,
        string? typeFilter = null, bool? filterByDuration = null, bool? ignoreVideo = null)
    {
        var query = files.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            query = query.Where(f =>
                f.FileName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 ||
                f.Title.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 ||
                f.Artist.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 ||
                f.Album.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        if (!string.IsNullOrWhiteSpace(typeFilter))
        {
            query = query.Where(f =>
                f.Extension.Equals(typeFilter.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (ignoreVideo == true)
        {
            query = query.Where(f => !IsVideoFile(f.Extension));
        }

        return query.ToList();
    }

    public IReadOnlyList<MusicFile> SortFiles(
        IEnumerable<MusicFile> files, string sortField = "FileName", bool ascending = true)
    {
        var sorted = sortField.ToLowerInvariant() switch
        {
            "filename" => files.OrderBy(f => f.FileName),
            "directory" => files.OrderBy(f => f.Directory),
            "audioformat" => files.OrderBy(f => f.AudioFormat),
            "title" => files.OrderBy(f => f.Title),
            "artist" => files.OrderBy(f => f.Artist).ThenBy(f => f.Title),
            "album" => files.OrderBy(f => f.Album).ThenBy(f => f.Track),
            "albumartist" => files.OrderBy(f => f.AlbumArtist),
            "year" => files.OrderBy(f => f.Year),
            "track" => files.OrderBy(f => f.Track),
            "disc" => files.OrderBy(f => f.Disc),
            "genre" => files.OrderBy(f => f.Genre),
            "composer" => files.OrderBy(f => f.Composer),
            "lyricist" => files.OrderBy(f => f.Lyricist),
            "comment" => files.OrderBy(f => f.Comment),
            "channels" => files.OrderBy(f => f.Channels),
            "samplerate" => files.OrderBy(f => f.SampleRate),
            "bitrate" => files.OrderBy(f => f.BitRate),
            "bitspersample" => files.OrderBy(f => f.BitsPerSample),
            "duration" => files.OrderBy(f => f.Duration),
            "filesize" => files.OrderBy(f => f.FileSize),
            "lastmodified" => files.OrderBy(f => f.LastModified),
            _ => files.OrderBy(f => f.FileName)
        };
        return (ascending ? sorted : sorted.Reverse()).ToList();
    }

    public async Task<int> DeleteFilesAsync(
        IEnumerable<MusicFile> files, IProgress<int>? progress = null)
    {
        var list = files.ToList();
        var count = 0;
        for (var i = 0; i < list.Count; i++)
        {
            try
            {
                File.Delete(list[i].FilePath);
                count++;
            }
            catch { /* skip errors */ }
            progress?.Report(i + 1);
        }
        return await Task.FromResult(count);
    }

    public async Task<int> RenameFilesAsync(
        IEnumerable<MusicFile> files,
        Func<MusicFile, string> nameGenerator, IProgress<int>? progress = null)
    {
        var list = files.ToList();
        var count = 0;
        for (var i = 0; i < list.Count; i++)
        {
            try
            {
                var f = list[i];
                var newName = nameGenerator(f);
                if (string.IsNullOrEmpty(newName)) continue;

                var dir = Path.GetDirectoryName(f.FilePath)!;
                var newPath = Path.Combine(dir, newName + f.Extension);
                if (newPath == f.FilePath) continue;

                File.Move(f.FilePath, newPath);
                count++;
            }
            catch { /* skip errors */ }
            progress?.Report(i + 1);
        }
        return await Task.FromResult(count);
    }

    public ISet<string> GetSupportedExtensions() => new HashSet<string>(DefaultExtensions);

    public bool IsSupportedFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return DefaultExtensions.Contains(ext);
    }

    private static bool IsVideoFile(string ext) =>
        ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".webm";

    private static void ApplyTags(MusicFile mf, TagData tags)
    {
        mf.Title = tags.Title ?? "";
        mf.Artist = tags.Artist ?? "";
        mf.Album = tags.Album ?? "";
        mf.Year = tags.Year;
        mf.Track = tags.Track;
        mf.TrackCount = tags.TrackCount ?? 0;
        mf.Genre = tags.Genre ?? "";
        mf.Comment = tags.Comment ?? "";
        mf.AlbumArtist = tags.AlbumArtist ?? "";
        mf.Composer = tags.Composer ?? "";
        mf.Lyricist = tags.Lyricist ?? "";
        mf.Disc = tags.Disc;
        mf.DiscCount = tags.DiscCount ?? 0;
        mf.HasLyrics = tags.Lyrics != null;
        mf.Lyrics = tags.Lyrics;
        mf.HasCoverArt = tags.CoverArtData != null;
        mf.CoverArtData = tags.CoverArtData;
        mf.CoverArtMimeType = tags.CoverArtMimeType;
        mf.CoverArtType = tags.CoverArtType;

        // 音频属性
        if (tags.DurationMs.HasValue) mf.Duration = TimeSpan.FromMilliseconds(tags.DurationMs.Value);
        if (tags.BitRate.HasValue) mf.BitRate = tags.BitRate.Value;
        if (tags.SampleRate.HasValue) mf.SampleRate = tags.SampleRate.Value;
        if (tags.Channels.HasValue) mf.Channels = tags.Channels.Value;
        if (tags.BitsPerSample.HasValue) mf.BitsPerSample = tags.BitsPerSample.Value;
        if (tags.TagFormat != null) mf.TagFormat = tags.TagFormat;
    }
}
