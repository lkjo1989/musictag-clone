using Newtonsoft.Json;

namespace MusicTagClone.Models;

/// <summary>
/// 列头信息 — 用于自定义显示列的持久化（序列化为 JSON 存储在 ListviewColumnHeader 设置中）。
/// </summary>
[Serializable]
public class ColumnHeaderInfo
{
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("width")]
    public int Width { get; set; }

    [JsonProperty("displayIndex")]
    public int DisplayIndex { get; set; }

    [JsonProperty("isShow")]
    public bool IsShow { get; set; } = true;

    [JsonProperty("textAlign")]
    public HorizontalAlignment TextAlign { get; set; } = HorizontalAlignment.Left;

    [JsonProperty("beforeHideDisplayIndex")]
    public int BeforeHideDisplayIndex { get; set; }

    /// <summary>临时宽度（对话框用，隐藏再显示时恢复）</summary>
    [JsonIgnore]
    public int TempWidth { get; set; }

    public ColumnHeaderInfo() { }

    public ColumnHeaderInfo(string name, int width, int displayIndex,
        HorizontalAlignment textAlign = HorizontalAlignment.Left, bool isShow = true)
    {
        Name = name;
        Width = width;
        DisplayIndex = displayIndex;
        TextAlign = textAlign;
        IsShow = isShow;
    }

    /// <summary>
    /// 所有可用列的列名列表（定义顺序即默认显示顺序）。
    /// </summary>
    public static readonly string[] AllColumnNames =
    {
        "filename", "filedir", "tagtypes", "title", "artist", "album",
        "albumartist", "year", "trackstr", "discstr", "genre",
        "composer", "lyricist", "comment", "haspicture", "lyrics",
        "channels", "samplerate", "bitrate", "bitpersample", "durationinms", "updatetime"
    };

    /// <summary>列名 → 显示文本</summary>
    public static readonly Dictionary<string, string> DisplayNames = new()
    {
        ["filename"] = "文件名",
        ["filedir"] = "目录",
        ["tagtypes"] = "标签格式",
        ["title"] = "标题",
        ["artist"] = "艺术家",
        ["album"] = "专辑",
        ["albumartist"] = "专辑艺术家",
        ["year"] = "年份",
        ["trackstr"] = "音轨号",
        ["discstr"] = "碟号",
        ["genre"] = "风格",
        ["composer"] = "作曲家",
        ["lyricist"] = "作词家",
        ["comment"] = "注释",
        ["haspicture"] = "封面",
        ["lyrics"] = "歌词",
        ["channels"] = "声道",
        ["samplerate"] = "采样率",
        ["bitrate"] = "比特率",
        ["bitpersample"] = "位深",
        ["durationinms"] = "时长",
        ["updatetime"] = "修改时间",
    };

    /// <summary>列名 → 默认宽度</summary>
    public static readonly Dictionary<string, int> DefaultWidths = new()
    {
        ["filename"] = 200, ["filedir"] = 160, ["tagtypes"] = 80,
        ["title"] = 150, ["artist"] = 120, ["album"] = 120,
        ["albumartist"] = 100, ["year"] = 60, ["trackstr"] = 60,
        ["discstr"] = 50, ["genre"] = 80, ["composer"] = 100,
        ["lyricist"] = 100, ["comment"] = 100, ["haspicture"] = 50,
        ["lyrics"] = 60, ["channels"] = 50, ["samplerate"] = 70,
        ["bitrate"] = 70, ["bitpersample"] = 60, ["durationinms"] = 70,
        ["updatetime"] = 120,
    };

    /// <summary>列名 → 默认对齐方式</summary>
    public static readonly Dictionary<string, HorizontalAlignment> DefaultAlignments = new()
    {
        ["haspicture"] = HorizontalAlignment.Center,
        ["channels"] = HorizontalAlignment.Right,
        ["samplerate"] = HorizontalAlignment.Right,
        ["bitrate"] = HorizontalAlignment.Right,
        ["bitpersample"] = HorizontalAlignment.Right,
        ["durationinms"] = HorizontalAlignment.Right,
        ["year"] = HorizontalAlignment.Right,
        ["trackstr"] = HorizontalAlignment.Right,
        ["discstr"] = HorizontalAlignment.Right,
        ["updatetime"] = HorizontalAlignment.Right,
    };

    /// <summary>创建默认列设置列表</summary>
    public static List<ColumnHeaderInfo> CreateDefaults()
    {
        var list = new List<ColumnHeaderInfo>();
        int displayIndex = 0;
        foreach (var name in AllColumnNames)
        {
            DefaultWidths.TryGetValue(name, out var width);
            DefaultAlignments.TryGetValue(name, out var align);
            list.Add(new ColumnHeaderInfo(name, width, displayIndex++, align));
        }
        return list;
    }
}
