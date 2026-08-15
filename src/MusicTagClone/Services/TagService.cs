using MusicTagClone.Interfaces;
using MusicTagClone.Models;
using TagLib;
using TagLib.Id3v2;
#if NET6_0_OR_GREATER || NETFRAMEWORK
using MediaInfo;
using MediaInfo.Model;
#endif

namespace MusicTagClone.Services;

/// <summary>
/// 标签读写服务 — 读取使用 MediaInfo（net10.0 用 MediaInfo.Wrapper.Core，
/// net461 用 MediaInfo.Wrapper），写入统一使用 TagLibSharp。
///
/// 为何读和写用不同库？
/// - MediaInfo 对畸形/非标 MP4 容器的容错性远强于 TagLibSharp（同 VLC/FFmpeg），
///   但 MediaInfo 是只读库。net10.0 和 net461 都走 MediaInfo 优先、TagLibSharp 兜底。
/// - TagLibSharp 支持所有格式的标准标签写入。
/// - 对于 MediaInfo 能读但 TagLibSharp 写不进去的畸形 M4A 文件，
///   M4aTagFixer 会在写入前重建标准 ilst box，使 TagLibSharp 能正常写入。
/// </summary>
public class TagService : ITagService
{
    private readonly ILoggerService _logger;
    private static readonly HashSet<string> SupportedExtensions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".mp4", ".ogg", ".wma", ".wav",
        ".aiff", ".aif", ".ape", ".wv", ".mpc", ".opus", ".dsf", ".dff"
    };

    public TagService(ILoggerService logger)
    {
        _logger = logger;
    }

    public Task<TagData> ReadTagsAsync(string filePath)
    {
        return Task.Run(() =>
        {
#if NET6_0_OR_GREATER || NETFRAMEWORK
            try
            {
                var mi = new MediaInfoWrapper(filePath);
                if (mi.Success && mi.AudioStreams.Count > 0)
                {
                    var audio = mi.AudioStreams.FirstOrDefault();
                    var result = MapMediaInfoToTagData(mi, audio?.Tags);
                    // MediaInfo 不返回封面和歌词，用 TagLibSharp 补充
                    try
                    {
                        using var tagFile = TagLib.File.Create(filePath);
                        var tag = MapToTagData(tagFile);
                        if (tag.CoverArtData != null) result.CoverArtData = tag.CoverArtData;
                        if (tag.CoverArtMimeType != null) result.CoverArtMimeType = tag.CoverArtMimeType;
                        if (tag.CoverArtType != null) result.CoverArtType = tag.CoverArtType;
                        if (tag.AllPictures != null) result.AllPictures = tag.AllPictures;
                        if (tag.Lyrics != null) result.Lyrics = tag.Lyrics;
                    }
                    catch { /* TagLibSharp 补充失败不影响 MediaInfo 已获取的数据 */ }
                    return result;
                }
                // MediaInfo 无法解析（如测试用的微型 MP3），回退到 TagLibSharp
                return ReadTagsViaTagLib(filePath);
            }
            catch
            {
                return new TagData();
            }
#else
            try
            {
                using var file = TagLib.File.Create(filePath);
                return MapToTagData(file);
            }
            catch
            {
                return new TagData();
            }
#endif
        });
    }

    /// <summary>从 MediaInfo 输出提取歌词文本</summary>
    public Task<string?> ReadLyricsAsync(string filePath)
    {
        return Task.Run(() =>
        {
#if NET6_0_OR_GREATER || NETFRAMEWORK
            try
            {
                var mi = new MediaInfoWrapper(filePath);
                if (mi.Success && !string.IsNullOrEmpty(mi.Text))
                {
                    var lines = mi.Text.Split('\n');
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("Lyrics", StringComparison.OrdinalIgnoreCase)
                            || trimmed.StartsWith("LYRICS", StringComparison.Ordinal))
                        {
                            var idx = trimmed.IndexOf(':');
                            if (idx > 0)
                                return trimmed.Substring(idx + 1).TrimStart();
                        }
                    }
                }
                // 回退到 TagLibSharp
                return TagLibReadLyrics(filePath);
            }
            catch
            {
                return null;
            }
#else
            try
            {
                using var file = TagLib.File.Create(filePath);
                return string.IsNullOrEmpty(file.Tag.Lyrics) ? null : file.Tag.Lyrics;
            }
            catch
            {
                return null;
            }
#endif
        });
    }

    /// <summary>TagLibSharp 读取标签（MediaInfo 无法解析时的回退路径）</summary>
    private static TagData ReadTagsViaTagLib(string filePath)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);
            return MapToTagData(file);
        }
        catch
        {
            return new TagData();
        }
    }

    /// <summary>TagLibSharp 读取歌词（MediaInfo 无法解析时的回退路径）</summary>
    private static string? TagLibReadLyrics(string filePath)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);
            return string.IsNullOrEmpty(file.Tag.Lyrics) ? null : file.Tag.Lyrics;
        }
        catch
        {
            return null;
        }
    }

#if NET6_0_OR_GREATER || NETFRAMEWORK
    /// <summary>MediaInfo → TagData 映射</summary>
    private static TagData MapMediaInfoToTagData(MediaInfoWrapper mi, AudioTags? tags)
    {
        var result = new TagData();

        if (tags != null)
        {
            result.Title = NullIfEmpty(tags.Title);
            result.Artist = NullIfEmpty(tags.Artist);
            result.Album = NullIfEmpty(tags.Album);
            result.AlbumArtist = NullIfEmpty(tags.AlbumArtist);
            result.Genre = NullIfEmpty(tags.Genre);
            result.Comment = NullIfEmpty(tags.Comment);
            result.Composer = NullIfEmpty(tags.Composer);
            result.Lyricist = NullIfEmpty(tags.Lyricist);

            if (tags.RecordedDate.HasValue)
            {
                var year = (uint?)tags.RecordedDate.Value.Year;
                if (year > 0) result.Year = year;
            }
            if (tags.TrackPosition.HasValue && tags.TrackPosition.Value > 0)
                result.Track = (uint?)tags.TrackPosition.Value;
            if (tags.TotalTracks.HasValue && tags.TotalTracks.Value > 0)
                result.TrackCount = (uint?)tags.TotalTracks.Value;
            if (tags.DiscNumber.HasValue && tags.DiscNumber.Value > 0)
                result.Disc = (uint?)tags.DiscNumber.Value;
            if (tags.TotalDiscs.HasValue && tags.TotalDiscs.Value > 0)
                result.DiscCount = (uint?)tags.TotalDiscs.Value;
        }

        var audioStream = mi.AudioStreams.FirstOrDefault();
        if (mi.Duration > 0) result.DurationMs = mi.Duration;
        if (mi.AudioSampleRate > 0) result.SampleRate = mi.AudioSampleRate;
        if (mi.AudioRate > 0) result.BitRate = mi.AudioRate;
        if (mi.AudioChannels > 0) result.Channels = mi.AudioChannels;
        if (audioStream?.BitDepth > 0) result.BitsPerSample = audioStream.BitDepth;

        if (!string.IsNullOrEmpty(mi.Format))
        {
            var ver = !string.IsNullOrEmpty(mi.Version) ? $" v{mi.Version}" : "";
            result.TagFormat = $"{mi.Format}{ver}";
        }

        return result;
    }
#endif

    public async Task<IReadOnlyList<TagData>> ReadTagsBatchAsync(IEnumerable<string> filePaths)
    {
        var results = new List<TagData>();
        foreach (var path in filePaths)
        {
            results.Add(await ReadTagsAsync(path));
        }
        return results;
    }

    public Task<bool> WriteTagsAsync(string filePath, TagData tags, bool keepUpdateTime = false)
    {
        return Task.Run(() =>
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var lastWrite = fileInfo.LastWriteTime;

                using var file = TagLib.File.Create(filePath);
                ApplyTagData(file, tags);

                if (keepUpdateTime)
                {
                    file.Save();
                    System.IO.File.SetLastWriteTime(filePath, lastWrite);
                }
                else
                {
                    file.Save();
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "TagService.WriteTagsAsync 失败: {0}", filePath);

                // TagLibSharp 无法解析该文件（如畸形 M4A）：修复标签区后重试
                if (filePath.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)
                    || filePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    return WriteM4aViaFixer(filePath, tags, keepUpdateTime);
                }
                return false;
            }
        });
    }

    /// <summary>对 TagLibSharp 无法解析的 M4A 文件：修复 ilst box，再用 TagLibSharp 写入</summary>
    private bool WriteM4aViaFixer(string filePath, TagData tags, bool keepUpdateTime)
    {
        try
        {
            // 1. 生成标准 ilst 替换文件中的畸形标签区
            if (!Utils.M4aTagFixer.TryFix(filePath, tags))
                return false;

            // 2. 现在 TagLibSharp 应能正常打开
            var fileInfo = new FileInfo(filePath);
            var lastWrite = fileInfo.LastWriteTime;

            using var file = TagLib.File.Create(filePath);

            // 应用标签（写入刚刚放入 ilst 的值，保证所有格式都正确）
            ApplyTagData(file, tags);

            if (keepUpdateTime)
                file.Save();
            else
                file.Save();

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "WriteM4aViaFixer 也失败: {0}", filePath);
            return false;
        }
    }

    public async Task<int> WriteTagsBatchAsync(
        IEnumerable<KeyValuePair<string, TagData>> fileTags,
        bool keepUpdateTime = false, IProgress<int>? progress = null)
    {
        var items = fileTags.ToList();
        var successCount = 0;

        for (var i = 0; i < items.Count; i++)
        {
            var success = await WriteTagsAsync(items[i].Key, items[i].Value, keepUpdateTime);
            if (success) successCount++;
            progress?.Report(i + 1);
        }

        return successCount;
    }

    public Task<bool> ClearTagsAsync(string filePath)
    {
        return WriteTagsAsync(filePath, new TagData
        {
            Title = "",
            Artist = "",
            Album = "",
            Year = 0,
            Track = 0,
            TrackCount = 0,
            Genre = "",
            Comment = "",
            AlbumArtist = "",
            Composer = "",
            Disc = 0,
            DiscCount = 0,
            Lyrics = ""
        });
    }

    public Task<bool> WriteCoverArtAsync(string filePath, CoverArt cover)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!cover.HasImage) return false;

                using var file = TagLib.File.Create(filePath);
                file.Tag.Pictures = new IPicture[]
                {
                    new TagLib.Picture(new ByteVector(cover.ImageData))
                    {
                        MimeType = cover.MimeType ?? "image/jpeg",
                        Type = PictureType.FrontCover
                    }
                };
                file.Save();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    public Task<CoverArt?> ReadCoverArtAsync(string filePath)
    {
        return Task.Run(() =>
        {
            try
            {
                using var file = TagLib.File.Create(filePath);
                var pic = file.Tag.Pictures.FirstOrDefault();
                if (pic == null) return null;

                return new CoverArt
                {
                    ImageData = pic.Data.Data,
                    MimeType = pic.MimeType
                };
            }
            catch (Exception)
            {
                return null;
            }
        });
    }

    public Task<bool> WriteLyricsAsync(string filePath, string lyrics)
    {
        return Task.Run(() =>
        {
            try
            {
                using var file = TagLib.File.Create(filePath);
                file.Tag.Lyrics = lyrics;
                file.Save();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    #region Private Helpers

    private static TagData MapToTagData(TagLib.File file)
    {
        var tag = file.Tag;
        var props = file.Properties;

        // 读取所有图片
        List<CoverArt>? allPics = null;
        if (tag.Pictures != null && tag.Pictures.Length > 0)
        {
            allPics = new List<CoverArt>(tag.Pictures.Length);
            foreach (var pic in tag.Pictures)
            {
                if (pic.Data != null && pic.Data.Count > 0)
                {
                    allPics.Add(new CoverArt
                    {
                        ImageData = pic.Data.Data,
                        MimeType = pic.MimeType,
                    });
                }
            }
        }

        return new TagData
        {
            Title = NullIfEmpty(tag.Title),
            Artist = NullIfEmpty(tag.FirstPerformer),
            Album = NullIfEmpty(tag.Album),
            Year = tag.Year,
            Track = tag.Track,
            TrackCount = tag.TrackCount,
            Genre = NullIfEmpty(tag.FirstGenre),
            Comment = NullIfEmpty(tag.Comment),
            AlbumArtist = NullIfEmpty(tag.FirstAlbumArtist),
            Composer = NullIfEmpty(tag.FirstComposer),
            Lyricist = ReadLyricist(file),
            Disc = tag.Disc,
            DiscCount = tag.DiscCount,
            Lyrics = NullIfEmpty(tag.Lyrics),
            CoverArtMimeType = tag.Pictures.FirstOrDefault()?.MimeType,
            CoverArtData = tag.Pictures.FirstOrDefault()?.Data.Data,
            CoverArtType = PicTypeToName(tag.Pictures.FirstOrDefault()?.Type),
            AllPictures = allPics,
            DurationMs = (long?)props?.Duration.TotalMilliseconds,
            BitRate = props?.AudioBitrate,
            SampleRate = props?.AudioSampleRate,
            Channels = props?.AudioChannels,
            BitsPerSample = props?.BitsPerSample > 0 ? props.BitsPerSample : null,
            TagFormat = FormatTagTypes(file),
        };

        string FormatTagTypes(TagLib.File f)
        {
            var tags = f.TagTypes;
            var parts = new List<string>();
            if ((tags & TagLib.TagTypes.Id3v2) != 0)
            {
                if (f.Tag is TagLib.Id3v2.Tag id3v2)
                    parts.Add($"ID3v2.{id3v2.Version}");
                else
                    parts.Add("ID3v2");
            }
            if ((tags & TagLib.TagTypes.Id3v1) != 0) parts.Add("ID3v1");
            if ((tags & TagLib.TagTypes.Ape) != 0) parts.Add("APE");
            if ((tags & TagLib.TagTypes.FlacMetadata) != 0) parts.Add("FLAC");
            if ((tags & TagLib.TagTypes.Xiph) != 0) parts.Add("Vorbis");
            if ((tags & TagLib.TagTypes.Apple) != 0) parts.Add("MP4");
            if ((tags & TagLib.TagTypes.Asf) != 0) parts.Add("WMA");
            if ((tags & TagLib.TagTypes.RiffInfo) != 0) parts.Add("RIFF");
            if ((tags & TagLib.TagTypes.MovieId) != 0) parts.Add("Movie");
            return parts.Count > 0 ? string.Join(" + ", parts) : f.Properties?.Description ?? "";
        }
    }
    private static void ApplyTagData(TagLib.File file, TagData tags)
    {
        var tag = file.Tag;
        if (tags.Title != null) tag.Title = tags.Title;
        if (tags.Artist != null) tag.Performers = new[] { tags.Artist };
        if (tags.Album != null) tag.Album = tags.Album;
        if (tags.Year.HasValue) tag.Year = tags.Year.Value;
        if (tags.Track.HasValue) tag.Track = tags.Track.Value;
        if (tags.TrackCount.HasValue) tag.TrackCount = tags.TrackCount.Value;
        if (tags.Genre != null) tag.Genres = new[] { tags.Genre };
        if (tags.Comment != null) tag.Comment = tags.Comment;
        if (tags.AlbumArtist != null) tag.AlbumArtists = new[] { tags.AlbumArtist };
        if (tags.Composer != null) tag.Composers = new[] { tags.Composer };
        if (tags.Lyricist != null) WriteLyricist(file, tags.Lyricist);
        if (tags.Disc.HasValue) tag.Disc = tags.Disc.Value;
        if (tags.DiscCount.HasValue) tag.DiscCount = tags.DiscCount.Value;
        if (tags.Lyrics != null) tag.Lyrics = tags.Lyrics;

        if (tags.AllPictures != null && tags.AllPictures.Count > 0)
        {
            tag.Pictures = tags.AllPictures
                .Where(p => p.HasImage)
                .Select(p => (IPicture)new TagLib.Picture(new ByteVector(p.ImageData))
                {
                    MimeType = p.MimeType ?? "image/jpeg",
                    Type = PictureType.FrontCover
                })
                .ToArray();
        }
        else if (tags.CoverArtData != null)
        {
            tag.Pictures = new IPicture[]
            {
                new TagLib.Picture(new ByteVector(tags.CoverArtData))
                {
                    MimeType = tags.CoverArtMimeType ?? "image/jpeg",
                    Type = PictureType.FrontCover
                }
            };
        }
    }

    private static string? ReadLyricist(TagLib.File file)
    {
        // ID3v2 (MP3, some FLAC/APE) — TEXT frame
        if ((file.TagTypes & TagLib.TagTypes.Id3v2) != 0)
        {
            var id3v2 = file.GetTag(TagLib.TagTypes.Id3v2) as TagLib.Id3v2.Tag;
            if (id3v2 != null)
            {
                var frames = id3v2.GetFrames<TagLib.Id3v2.TextInformationFrame>("TEXT");
                var text = frames.FirstOrDefault()?.Text;
                if (text != null && text.Length > 0 && !string.IsNullOrEmpty(text[0]))
                    return text[0];
            }
        }

        // Xiph Comment (OGG, FLAC, Opus) — LYRICIST field
        if (file.Tag is TagLib.Ogg.XiphComment xiph)
        {
            var fields = xiph.GetField("LYRICIST");
            if (fields.Length > 0 && !string.IsNullOrEmpty(fields[0]))
                return fields[0];
        }

        // APE tag (APE, MPC, WavPack) — Lyricist field
        if (file.Tag is TagLib.Ape.Tag ape)
        {
            var item = ape.GetItem("Lyricist");
            if (item != null && !string.IsNullOrEmpty(item.ToString()))
                return item.ToString();
        }

        // ASF/WMA — WM/Lyricist field
        if (file.Tag is TagLib.Asf.Tag asf)
        {
            var descriptor = asf.ExtendedContentDescriptionObject
                .GetDescriptors(new[] { "WM/Lyricist" }).FirstOrDefault();
            if (descriptor != null)
            {
                var val = descriptor.ToString();
                if (!string.IsNullOrEmpty(val))
                    return val;
            }
        }

        return null;
    }

    private static void WriteLyricist(TagLib.File file, string lyricist)
    {
        if (string.IsNullOrEmpty(lyricist)) return;

        // ID3v2 (MP3, some FLAC/APE) — TEXT frame
        if ((file.TagTypes & TagLib.TagTypes.Id3v2) != 0)
        {
            var id3v2 = file.GetTag(TagLib.TagTypes.Id3v2) as TagLib.Id3v2.Tag;
            if (id3v2 != null)
            {
                var frame = TagLib.Id3v2.TextInformationFrame.Get(id3v2, "TEXT", true);
                frame.Text = new[] { lyricist };
                return;
            }
        }

        // Xiph Comment (OGG, FLAC, Opus) — LYRICIST field
        if (file.Tag is TagLib.Ogg.XiphComment xiph)
        {
            xiph.SetField("LYRICIST", new[] { lyricist });
            return;
        }

        // APE tag (APE, MPC, WavPack) — Lyricist field
        if (file.Tag is TagLib.Ape.Tag ape)
        {
            ape.SetValue("Lyricist", lyricist);
            return;
        }

        // ASF/WMA — WM/Lyricist field
        if (file.Tag is TagLib.Asf.Tag asf)
        {
            asf.ExtendedContentDescriptionObject.SetDescriptors("WM/Lyricist",
                new[] { new TagLib.Asf.ContentDescriptor("WM/Lyricist", lyricist) });
            return;
        }
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrEmpty(s) ? null : s;

    private static string? PicTypeToName(TagLib.PictureType? type)
    {
        if (type == null) return null;
        return type switch
        {
            TagLib.PictureType.FrontCover => "封面",
            TagLib.PictureType.BackCover => "封底",
            TagLib.PictureType.LeafletPage => "插页",
            TagLib.PictureType.Media => "介质",
            TagLib.PictureType.LeadArtist => "主要艺术家",
            TagLib.PictureType.Artist => "艺术家",
            TagLib.PictureType.Conductor => "指挥",
            TagLib.PictureType.Band => "乐队",
            TagLib.PictureType.Composer => "作曲家",
            TagLib.PictureType.Lyricist => "作词家",
            TagLib.PictureType.RecordingLocation => "录制地点",
            TagLib.PictureType.DuringRecording => "录制中",
            TagLib.PictureType.DuringPerformance => "表演中",
            TagLib.PictureType.MovieScreenCapture => "电影截图",
            TagLib.PictureType.Illustration => "插图",
            TagLib.PictureType.BandLogo => "乐队标志",
            TagLib.PictureType.PublisherLogo => "出版商标志",
            TagLib.PictureType.FileIcon => "文件图标",
            TagLib.PictureType.OtherFileIcon => "其他文件图标",
            TagLib.PictureType.ColoredFish => "彩色鱼",
            TagLib.PictureType.NotAPicture => "非图片",
            _ => "其他"
        };
    }

    #endregion
}
