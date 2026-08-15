namespace MusicTagClone.Models;

/// <summary>
/// 音乐文件实体，包含文件路径、音频属性和标签信息
/// </summary>
public class MusicFile
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public string Directory => Path.GetDirectoryName(FilePath) ?? string.Empty;
    public string Extension => Path.GetExtension(FilePath).ToLowerInvariant();
    public long FileSize { get; set; }
    public DateTime LastModified { get; set; }
    public TimeSpan Duration { get; set; }

    // 音频属性
    public int BitRate { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int BitsPerSample { get; set; }
    public string AudioFormat { get; set; } = string.Empty;

    // 标签格式（如 "ID3v2.3", "FLAC", "APE"）
    public string? TagFormat { get; set; }

    // 标签信息
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public uint? Year { get; set; }
    public uint? Track { get; set; }
    public uint? TrackCount { get; set; }
    public string Genre { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public string AlbumArtist { get; set; } = string.Empty;
    public string Composer { get; set; } = string.Empty;
    public string Lyricist { get; set; } = string.Empty;
    public uint? Disc { get; set; }
    public uint? DiscCount { get; set; }

    // 封面
    public bool HasCoverArt { get; set; }
    public string? CoverArtMimeType { get; set; }
    public byte[]? CoverArtData { get; set; }
    public string? CoverArtType { get; set; }
    public List<CoverArt>? AllPictures { get; set; }
    public int CurrentPictureIndex { get; set; }

    // 歌词
    public bool HasLyrics { get; set; }
    public string? Lyrics { get; set; }

    // 标签状态
    public bool IsModified { get; set; }
    public bool IsSelected { get; set; }
    public bool IsChecked { get; set; }

    /// <summary>
    /// 从文件路径创建 MusicFile，延迟加载标签信息
    /// </summary>
    public static MusicFile FromPath(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        return new MusicFile
        {
            FilePath = filePath,
            FileSize = fileInfo.Exists ? fileInfo.Length : 0,
            LastModified = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.MinValue
        };
    }

    public override string ToString() => $"{Artist} - {Title}".Trim(' ', '-');
}
