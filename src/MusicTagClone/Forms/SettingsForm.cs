using MusicTagClone.Interfaces;
using MusicTagClone.Models;

namespace MusicTagClone.Forms;

/// <summary>
/// 设置对话框 — 包含文件、搜索、标签源、歌词、图片、代理、缓存、日志等分类标签页
/// </summary>
public partial class SettingsForm : Form
{
    private readonly ISettingsService _settings;
    private readonly ILoggerService? _logger;

    // === 代理标签页 ===
    private TabPage proxyTab;
    private TextBox proxyUrlBox;
    private Dictionary<string, CheckBox> proxySourceChecks = new();

    // === 标签源标签页 ===
    private TabPage sourceTab;
    private CheckedListBox pictureSourceList;
    private CheckedListBox lyricSourceList;
    private CheckedListBox combTagsSourceList;

    // === 歌词标签页 ===
    private TabPage lyricTab;
    private CheckBox reformatTimetagCheck;
    private CheckBox removeTimetagCheck;
    private CheckBox deleteHeadTagCheck;
    private CheckBox deleteBlankLinesCheck;
    private CheckBox downloadTransCheck;
    private CheckBox dontDownloadOrigCheck;
    private TextBox lrcFilenameFormatBox;
    private TextBox saveLrcDirBox;
    private Button browseLrcDirBtn;
    private ComboBox lrcEncodingCombo;

    // === 图片标签页 ===
    private TabPage pictureTab;
    private TextBox pictureFormatsBox;
    private NumericUpDown pictureMaxRes;
    private NumericUpDown pictureMaxSize;
    private CheckBox overwriteCoverCheck;

    // === 搜索标签页 ===
    private TabPage searchTab;
    private CheckedListBox searchConditionList;
    private NumericUpDown searchItemsLimit;
    private NumericUpDown searchThreadsUpDown;
    private ComboBox itunesCountryCombo;

    // === 文件标签页 ===
    private TabPage fileTab;
    private CheckBox includeSubDirCheck;
    private TextBox restrictExtsBox;
    private CheckBox ignoreVideoCheck;

    // === 日志标签页 ===
    private TabPage logTab;
    private CheckBox logEnabledCheck;
    private ComboBox logLevelCombo;
    private TextBox logFilePathBox;
    private Button browseLogPathBtn;

    // === 缓存标签页 ===
    private TabPage cacheTab;
    private Label historyPathLabel;
    private Label historySizeLabel;
    private Button clearHistoryBtn;
    private Button openHistoryDirBtn;
    private Label urlCachePathLabel;
    private Label urlCacheSizeLabel;
    private Button clearUrlCacheBtn;
    private Button openUrlCacheDirBtn;
    private NumericUpDown urlCacheMaxSizeUpDown;

    private TabControl tabControl;
    private Button okBtn;
    private Button cancelBtn;

    private readonly IImageCache _imageCache;

    public SettingsForm(ISettingsService settings, IImageCache imageCache,
        ILoggerService? logger = null)
    {
        _settings = settings;
        _imageCache = imageCache;
        _logger = logger;
        InitializeComponent();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        Text = "设置";
        Size = new Size(560, 620);
        MinimumSize = new Size(560, 620);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(12, 4)
        };

        fileTab = new TabPage("文件");
        InitFileTab();
        tabControl.TabPages.Add(fileTab);

        searchTab = new TabPage("搜索");
        InitSearchTab();
        tabControl.TabPages.Add(searchTab);

        sourceTab = new TabPage("标签源");
        InitSourceTab();
        tabControl.TabPages.Add(sourceTab);

        lyricTab = new TabPage("歌词");
        InitLyricTab();
        tabControl.TabPages.Add(lyricTab);

        pictureTab = new TabPage("图片");
        InitPictureTab();
        tabControl.TabPages.Add(pictureTab);

        proxyTab = new TabPage("代理");
        InitProxyTab();
        tabControl.TabPages.Add(proxyTab);

        cacheTab = new TabPage("缓存");
        InitCacheTab();
        tabControl.TabPages.Add(cacheTab);

        logTab = new TabPage("日志");
        InitLogTab();
        tabControl.TabPages.Add(logTab);

        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 45,
            BackColor = Color.FromArgb(245, 246, 248)
        };

        okBtn = new Button
        {
            Text = "确定",
            Size = new Size(80, 28),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            FlatStyle = FlatStyle.Flat
        };
        okBtn.Click += OnOkClick;

        cancelBtn = new Button
        {
            Text = "取消",
            Size = new Size(80, 28),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            FlatStyle = FlatStyle.Flat
        };
        cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        buttonPanel.Resize += (s, e) =>
        {
            okBtn.Location = new Point(buttonPanel.Width - 180, 8);
            cancelBtn.Location = new Point(buttonPanel.Width - 90, 8);
        };
        okBtn.Location = new Point(buttonPanel.Width - 180, 8);
        cancelBtn.Location = new Point(buttonPanel.Width - 90, 8);

        buttonPanel.Controls.Add(okBtn);
        buttonPanel.Controls.Add(cancelBtn);

        Controls.Add(tabControl);
        Controls.Add(buttonPanel);
    }

    private static readonly (string Key, string Label)[] ProxySources = new[]
    {
        ("netease", "网易云音乐"),
        ("qq", "QQ音乐"),
        ("itunes", "iTunes"),
        ("kuwo", "酷我音乐"),
        ("lastfm", "Last.fm"),
        ("musicbrainz", "MusicBrainz"),
        ("discogs", "Discogs"),
        ("kugou", "酷狗音乐"),
    };

    private void InitProxyTab()
    {
        var proxyLabel = CreateLabel("代理地址:", 12, 20, 80);
        proxyUrlBox = CreateTextBox("http://127.0.0.1:7890", 100, 16, 300);

        var sourceGroup = new GroupBox
        {
            Text = "按源启用代理",
            Location = new Point(12, 50),
            Size = new Size(460, 260),
        };

        int y = 24;
        foreach (var (key, label) in ProxySources)
        {
            var cb = CreateCheckBox(label, 16, y);
            proxySourceChecks[key] = cb;
            sourceGroup.Controls.Add(cb);
            y += 28;
        }

        proxyTab.Controls.AddRange(new Control[] { proxyLabel, proxyUrlBox, sourceGroup });
    }

    private void InitSourceTab()
    {
        var hint = CreateLabel("勾选启用的来源，使用上移/下移调整自动匹配时的优先顺序。", 12, 10, 500);
        pictureSourceList = CreateSourceList("图片源", TagSourceCategory.Picture, 30);
        lyricSourceList = CreateSourceList("歌词源", TagSourceCategory.Lyrics, 195);
        combTagsSourceList = CreateSourceList("组合标签源", TagSourceCategory.CombinationTags, 360);
        sourceTab.Controls.Add(hint);
    }

    private CheckedListBox CreateSourceList(string title, TagSourceCategory category, int y)
    {
        var group = new GroupBox
        {
            Text = title,
            Location = new Point(10, y),
            Size = new Size(510, 155),
            ForeColor = Color.Black
        };
        var list = new CheckedListBox
        {
            Location = new Point(12, 22),
            Size = new Size(385, 122),
            // 选中文字只改变选中行，点击复选框区域才切换启用状态。
            CheckOnClick = false,
            IntegralHeight = false,
            FormattingEnabled = true,
            ForeColor = Color.Black
        };
        LoadSourceList(list, category);

        var up = CreateSourceOrderButton("上移", 408, 38);
        var down = CreateSourceOrderButton("下移", 408, 75);
        up.Click += (_, _) => MoveCheckedItem(list, -1);
        down.Click += (_, _) => MoveCheckedItem(list, 1);
        group.Controls.AddRange(new Control[] { list, up, down });
        sourceTab.Controls.Add(group);
        return list;
    }

    private void LoadSourceList(CheckedListBox list, TagSourceCategory category)
    {
        var json = category switch
        {
            TagSourceCategory.Picture => _settings.PictureInfo_SourceItemList,
            TagSourceCategory.Lyrics => _settings.LyricInfo_SourceItemList,
            _ => _settings.CombTagsInfo_SourceItemList
        };
        var sources = TagSourceCatalog.Load(json, category, _settings.WebSearchItemsLimit);
        LogSourceConfiguration("加载设置", category, sources);
        foreach (var source in sources)
            list.Items.Add(source, source.Enabled);
    }

    private void LogSourceConfiguration(string action, TagSourceCategory category,
        IEnumerable<TagSourceItem> sources)
    {
        _logger?.Debug("[标签源设置] {0}: 类别={1}, {2}", action, category,
            TagSourceCatalog.Describe(sources));
    }

    private static Button CreateSourceOrderButton(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(78, 28),
        FlatStyle = FlatStyle.Flat,
        ForeColor = Color.Black
    };

    private static void MoveCheckedItem(CheckedListBox list, int offset)
    {
        var index = list.SelectedIndex;
        var target = index + offset;
        if (index < 0 || target < 0 || target >= list.Items.Count) return;

        var item = list.Items[index];
        var isChecked = list.GetItemChecked(index);
        list.Items.RemoveAt(index);
        list.Items.Insert(target, item);
        list.SetItemChecked(target, isChecked);
        list.SelectedIndex = target;
    }

    private static string SerializeSourceList(CheckedListBox list)
    {
        var items = list.Items.Cast<TagSourceItem>().ToList();
        for (var i = 0; i < items.Count; i++)
        {
            items[i].Enabled = list.GetItemChecked(i);
            items[i].Sequence = i;
        }
        return TagSourceCatalog.Serialize(items);
    }

    private string SerializeSearchConditionList()
    {
        var items = searchConditionList.Items.Cast<SearchConditionItem>().ToList();
        for (var i = 0; i < items.Count; i++)
        {
            items[i].Enabled = searchConditionList.GetItemChecked(i);
            items[i].Sequence = i;
        }

        _settings.SearchConditionUseTitle = items.Any(item =>
            item.Key == SearchCondition.TitleKey && item.Enabled);
        _settings.SearchConditionUseArtist = items.Any(item =>
            item.Key == SearchCondition.ArtistKey && item.Enabled);
        _settings.SearchConditionUseAlbum = items.Any(item =>
            item.Key == SearchCondition.AlbumKey && item.Enabled);
        return SearchConditionCatalog.Serialize(items);
    }

    private void InitLyricTab()
    {
        reformatTimetagCheck = CreateCheckBox("重新格式化时间标签", 12, 20);
        removeTimetagCheck = CreateCheckBox("移除时间标签", 12, 50);
        deleteHeadTagCheck = CreateCheckBox("删除头部标签", 12, 80);
        deleteBlankLinesCheck = CreateCheckBox("删除空白行", 12, 110);
        downloadTransCheck = CreateCheckBox("启用翻译下载", 12, 140);
        dontDownloadOrigCheck = CreateCheckBox("不下载原版歌词", 12, 170);

        lrcFilenameFormatBox = CreateTextBox("{artist} - {title}.lrc", 120, 200, 280);
        var lrcFmtLabel = CreateLabel("LRC 文件名格式:", 12, 204, 100);

        saveLrcDirBox = CreateTextBox("", 120, 230, 220);
        browseLrcDirBtn = new Button { Text = "...", Location = new Point(342, 228), Width = 30 };
        browseLrcDirBtn.Click += (s, e) =>
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog(this) == DialogResult.OK)
                saveLrcDirBox.Text = dlg.SelectedPath;
        };
        var saveDirLabel = CreateLabel("保存目录:", 12, 234, 100);

        lrcEncodingCombo = CreateCombo(new[] { "utf-8", "utf-16", "gbk", "gb18030", "gb2312", "big5" }, 120, 260);

        lyricTab.Controls.AddRange(new Control[] {
            reformatTimetagCheck, removeTimetagCheck, deleteHeadTagCheck, deleteBlankLinesCheck,
            downloadTransCheck, dontDownloadOrigCheck,
            lrcFmtLabel, lrcFilenameFormatBox,
            saveDirLabel, saveLrcDirBox, browseLrcDirBtn,
            new Label { Text = "默认编码:", Location = new Point(12, 264), Width = 100 },
            lrcEncodingCombo });
    }

    private void InitPictureTab()
    {
        pictureFormatsBox = CreateTextBox("jpg,jpeg,png,bmp,gif", 120, 20, 280);
        var fmtLabel = CreateLabel("允许的格式:", 12, 24, 100);

        pictureMaxRes = new NumericUpDown
            { Location = new Point(120, 50), Width = 80, Minimum = 100, Maximum = 10000, Value = 3000 };
        var resLabel = CreateLabel("最大分辨率:", 12, 54, 100);

        pictureMaxSize = new NumericUpDown
            { Location = new Point(120, 80), Width = 80, Minimum = 10, Maximum = 10240, Value = 1024 };
        var sizeLabel = CreateLabel("最大大小(KB):", 12, 84, 100);

        overwriteCoverCheck = CreateCheckBox("覆盖已有封面", 12, 110);

        pictureTab.Controls.AddRange(new Control[] {
            fmtLabel, pictureFormatsBox,
            resLabel, pictureMaxRes,
            sizeLabel, pictureMaxSize,
            overwriteCoverCheck });
    }

    private void InitSearchTab()
    {
        var searchConditionGroup = new GroupBox
        {
            Text = "搜索条件（勾选并调整顺序）",
            Location = new Point(10, 10),
            Size = new Size(510, 155),
            ForeColor = Color.Black
        };
        searchConditionList = new CheckedListBox
        {
            Location = new Point(12, 22),
            Size = new Size(385, 106),
            CheckOnClick = false,
            IntegralHeight = false,
            FormattingEnabled = true,
            ForeColor = Color.Black
        };
        LoadSearchConditionList();

        var up = CreateSourceOrderButton("上移", 408, 38);
        var down = CreateSourceOrderButton("下移", 408, 75);
        up.Click += (_, _) => MoveCheckedItem(searchConditionList, -1);
        down.Click += (_, _) => MoveCheckedItem(searchConditionList, 1);
        var searchConditionHint = CreateLabel("全部取消时使用文件名搜索", 12, 130, 300);
        searchConditionGroup.Controls.AddRange(new Control[] {
            searchConditionList, up, down, searchConditionHint });

        searchItemsLimit = new NumericUpDown
            { Location = new Point(160, 185), Width = 60, Minimum = 1, Maximum = 50, Value = 10 };
        var limitLabel = CreateLabel("搜索结果数量:", 12, 189, 140);

        searchThreadsUpDown = new NumericUpDown
            { Location = new Point(160, 215), Width = 60, Minimum = 1, Maximum = AutoMatchOptions.MaxThreadCount, Value = 4 };
        var threadLabel = CreateLabel("搜索线程数(自动匹配时):", 12, 219, 140);

        itunesCountryCombo = CreateCombo(
            new[] { "CN", "US", "JP", "KR", "TW", "HK", "GB", "DE", "FR" }, 160, 245);

        searchTab.Controls.AddRange(new Control[] {
            searchConditionGroup,
            limitLabel, searchItemsLimit,
            threadLabel, searchThreadsUpDown,
            new Label { Text = "iTunes 地区:", Location = new Point(12, 249), Width = 140 },
            itunesCountryCombo });
    }

    private void LoadSearchConditionList()
    {
        var legacyOnlyFilename = _settings.SearchConditionUseOnlyFilename;
        var items = SearchConditionCatalog.Load(
            _settings.SearchConditionItemList,
            _settings.SearchConditionUseTitle && !legacyOnlyFilename,
            _settings.SearchConditionUseArtist && !legacyOnlyFilename,
            _settings.SearchConditionUseAlbum && !legacyOnlyFilename);
        foreach (var item in items)
            searchConditionList.Items.Add(item, item.Enabled);
    }

    private void InitFileTab()
    {
        includeSubDirCheck = CreateCheckBox("包含子目录", 12, 20);
        ignoreVideoCheck = CreateCheckBox("忽略视频文件", 12, 50);
        restrictExtsBox = CreateTextBox(".mp3;.flac;.m4a;.ogg;.wma;.wav;.ape", 120, 80, 280);
        var extLabel = CreateLabel("限制扩展名:", 12, 84, 100);

        fileTab.Controls.AddRange(new Control[] {
            includeSubDirCheck, ignoreVideoCheck,
            extLabel, restrictExtsBox });
    }

    private void InitLogTab()
    {
        logEnabledCheck = CreateCheckBox("启用日志记录", 12, 20);
        logEnabledCheck.CheckedChanged += (_, _) =>
        {
            logLevelCombo.Enabled = logEnabledCheck.Checked;
            logFilePathBox.Enabled = logEnabledCheck.Checked;
            browseLogPathBtn.Enabled = logEnabledCheck.Checked;
        };

        logLevelCombo = CreateCombo(new[] { "Debug", "Info", "Warn", "Error" }, 120, 50);
        var levelLabel = CreateLabel("日志级别:", 12, 54, 100);

        logFilePathBox = CreateTextBox("", 120, 80, 250);
        browseLogPathBtn = new Button { Text = "...", Location = new Point(372, 78), Width = 30 };
        browseLogPathBtn.Click += (s, e) =>
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "日志文件|*.log|所有文件|*.*",
                FileName = "MusicTagClone.log"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                logFilePathBox.Text = dlg.FileName;
        };
        var pathLabel = CreateLabel("日志文件路径:", 12, 84, 100);
        var pathHint = CreateLabel("留空则使用默认路径 (log/log-日期.log)", 120, 104, 300);
        pathHint.ForeColor = Color.Gray;

        logTab.Controls.AddRange(new Control[] {
            logEnabledCheck,
            levelLabel, logLevelCombo,
            pathLabel, logFilePathBox, browseLogPathBtn, pathHint });
    }

    private void InitCacheTab()
    {
        var historyTitle = CreateLabel("历史封面缓存", 12, 14, 200);
        historyPathLabel = CreateLabel("路径: " + _imageCache.HistoryDir, 12, 38, 460);
        historySizeLabel = CreateLabel("占用: 计算中...", 12, 58, 300);

        clearHistoryBtn = CreateCacheButton("清理未引用文件", 12, 80, 130);
        clearHistoryBtn.Click += (s, e) =>
        {
            _imageCache.ClearUnreferencedHistory();
            RefreshCacheSizes();
            MessageBox.Show(this, "已清理未被引用的历史封面文件。", "缓存",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        var clearAllHistoryBtn = CreateCacheButton("清空全部", 0, 80, 90);
        clearAllHistoryBtn.Location = new Point(clearHistoryBtn.Right + 8, 80);
        clearAllHistoryBtn.Click += (s, e) =>
        {
            if (MessageBox.Show(this,
                "将删除全部历史封面文件（含被引用的）。\n历史记录文本保留，但封面将无法再回显。是否继续？",
                "缓存", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _imageCache.ClearHistory();
            RefreshCacheSizes();
        };

        openHistoryDirBtn = CreateCacheButton("打开目录", 0, 80, 90);
        openHistoryDirBtn.Location = new Point(clearAllHistoryBtn.Right + 8, 80);
        openHistoryDirBtn.Click += (s, e) => OpenDir(_imageCache.HistoryDir);

        var historyHint = CreateLabel("“清理未引用文件”仅删孤儿；“清空全部”会删除所有封面，历史记录封面将不可回显。",
            12, 112, 470);
        historyHint.ForeColor = Color.Black;

        var urlTitle = CreateLabel("下载图片缓存", 12, 150, 200);
        urlCachePathLabel = CreateLabel("路径: " + _imageCache.UrlCacheDir, 12, 174, 460);
        urlCacheSizeLabel = CreateLabel("占用: 计算中...", 12, 194, 300);

        var maxSizeLabel = CreateLabel("容量上限(MB):", 12, 224, 100);
        urlCacheMaxSizeUpDown = new NumericUpDown
        {
            Minimum = 16,
            Maximum = 4096,
            Location = new Point(120, 220),
            Width = 80
        };

        clearUrlCacheBtn = CreateCacheButton("清空缓存", 12, 252, 100);
        clearUrlCacheBtn.Click += (s, e) =>
        {
            if (MessageBox.Show(this, "确定清空全部下载图片缓存？下次搜索会重新下载。", "缓存",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _imageCache.ClearUrlCache();
            RefreshCacheSizes();
        };

        openUrlCacheDirBtn = CreateCacheButton("打开目录", 0, 252, 90);
        openUrlCacheDirBtn.Location = new Point(clearUrlCacheBtn.Right + 8, 252);
        openUrlCacheDirBtn.Click += (s, e) => OpenDir(_imageCache.UrlCacheDir);

        var urlHint = CreateLabel("性能缓存，启动时按容量上限与 7 天未用自动清理；确定后立即按新上限清理一次。",
            12, 284, 470);
        urlHint.ForeColor = Color.Black;

        cacheTab.Controls.AddRange(new Control[] {
            historyTitle, historyPathLabel, historySizeLabel,
            clearHistoryBtn, clearAllHistoryBtn, openHistoryDirBtn, historyHint,
            urlTitle, urlCachePathLabel, urlCacheSizeLabel,
            maxSizeLabel, urlCacheMaxSizeUpDown,
            clearUrlCacheBtn, openUrlCacheDirBtn, urlHint });

        tabControl.SelectedIndexChanged += (s, e) =>
        {
            if (tabControl.SelectedTab == cacheTab) RefreshCacheSizes();
        };
    }

    private void RefreshCacheSizes()
    {
        historySizeLabel.Text = "占用: " + FormatBytes(_imageCache.GetHistorySize());
        urlCacheSizeLabel.Text = "占用: " + FormatBytes(_imageCache.GetUrlCacheSize());
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024L * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("F1") + " MB";
        return (bytes / (1024.0 * 1024 * 1024)).ToString("F2") + " GB";
    }

    private void OpenDir(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir)
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void LoadSettings()
    {
        proxyUrlBox.Text = _settings.ProxyUrl ?? "http://127.0.0.1:7890";
        LoadProxySourceSettings();

        reformatTimetagCheck.Checked = _settings.LyricDownload_ReformatTimetag;
        removeTimetagCheck.Checked = _settings.LyricDownload_RemoveTimetag;
        deleteHeadTagCheck.Checked = _settings.LyricDownload_DeleteHeadTag;
        deleteBlankLinesCheck.Checked = _settings.LyricDownload_DeleteLinesOfBlankText;
        downloadTransCheck.Checked = _settings.LyricDownload_DownloadTrans_Enable;
        dontDownloadOrigCheck.Checked = _settings.LyricDownload_DownloadTrans_DontDownloadOrigLyric;
        lrcFilenameFormatBox.Text = _settings.SaveLrcFilenameFormat ?? "";
        saveLrcDirBox.Text = _settings.SaveLrcDirectory ?? "";
        lrcEncodingCombo.Text = _settings.SaveLrcFileDefaultEncoding ?? "utf-8";

        pictureFormatsBox.Text = _settings.PictureFormatLimits ?? "";
        pictureMaxRes.Value = _settings.PictureResolutionLimits;
        pictureMaxSize.Value = _settings.PictureSizeLimitsKB;
        overwriteCoverCheck.Checked = _settings.OverwritePictureboxPicture;

        searchItemsLimit.Value = _settings.WebSearchItemsLimit;
        searchThreadsUpDown.Value = Math.Max(1, Math.Min(AutoMatchOptions.MaxThreadCount,
            _settings.AutoMatchTagsWebSearchThreadCount));
        itunesCountryCombo.Text = _settings.ItunesSearchParamsCountry;

        includeSubDirCheck.Checked = _settings.IncludeSubDir;
        ignoreVideoCheck.Checked = _settings.FileFilterIgnoreVideoFile;
        restrictExtsBox.Text = _settings.RestrictFileExts ?? "";

        logEnabledCheck.Checked = _settings.LogEnabled;
        logLevelCombo.Text = _settings.LogLevel ?? "Info";
        logFilePathBox.Text = _settings.LogFilePath ?? "";
        logLevelCombo.Enabled = _settings.LogEnabled;
        logFilePathBox.Enabled = _settings.LogEnabled;
        browseLogPathBtn.Enabled = _settings.LogEnabled;

        urlCacheMaxSizeUpDown.Value = ClampToRange(_settings.UrlCacheMaxSizeMb, 16, 4096, 256);
    }

    private static decimal ClampToRange(int value, int min, int max, int fallback)
    {
        if (value < min || value > max) return fallback;
        return value;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        // 先应用日志配置，保证本次保存标签源时的 DEBUG 记录立即生效。
        _settings.LogEnabled = logEnabledCheck.Checked;
        _settings.LogLevel = logLevelCombo.Text;
        _settings.LogFilePath = string.IsNullOrEmpty(logFilePathBox.Text) ? null : logFilePathBox.Text;

        _settings.ProxyUrl = proxyUrlBox.Text;
        SaveProxySourceSettings();
        _settings.PictureInfo_SourceItemList = SerializeSourceList(pictureSourceList);
        _settings.LyricInfo_SourceItemList = SerializeSourceList(lyricSourceList);
        _settings.CombTagsInfo_SourceItemList = SerializeSourceList(combTagsSourceList);
        LogSavedSourceConfiguration(TagSourceCategory.Picture, _settings.PictureInfo_SourceItemList);
        LogSavedSourceConfiguration(TagSourceCategory.Lyrics, _settings.LyricInfo_SourceItemList);
        LogSavedSourceConfiguration(TagSourceCategory.CombinationTags, _settings.CombTagsInfo_SourceItemList);

        _settings.LyricDownload_ReformatTimetag = reformatTimetagCheck.Checked;
        _settings.LyricDownload_RemoveTimetag = removeTimetagCheck.Checked;
        _settings.LyricDownload_DeleteHeadTag = deleteHeadTagCheck.Checked;
        _settings.LyricDownload_DeleteLinesOfBlankText = deleteBlankLinesCheck.Checked;
        _settings.LyricDownload_DownloadTrans_Enable = downloadTransCheck.Checked;
        _settings.LyricDownload_DownloadTrans_DontDownloadOrigLyric = dontDownloadOrigCheck.Checked;
        _settings.SaveLrcFilenameFormat = lrcFilenameFormatBox.Text;
        _settings.SaveLrcDirectory = saveLrcDirBox.Text;
        _settings.SaveLrcFileDefaultEncoding = lrcEncodingCombo.Text;

        _settings.PictureFormatLimits = pictureFormatsBox.Text;
        _settings.PictureResolutionLimits = (int)pictureMaxRes.Value;
        _settings.PictureSizeLimitsKB = (int)pictureMaxSize.Value;
        _settings.OverwritePictureboxPicture = overwriteCoverCheck.Checked;

        _settings.SearchConditionItemList = SerializeSearchConditionList();
        _settings.SearchConditionUseOnlyFilename = false;
        _settings.WebSearchItemsLimit = (int)searchItemsLimit.Value;
        _settings.AutoMatchTagsWebSearchThreadCount = (int)searchThreadsUpDown.Value;
        _settings.ItunesSearchParamsCountry = itunesCountryCombo.Text;

        _settings.IncludeSubDir = includeSubDirCheck.Checked;
        _settings.FileFilterIgnoreVideoFile = ignoreVideoCheck.Checked;
        _settings.RestrictFileExts = restrictExtsBox.Text;

        _settings.UrlCacheMaxSizeMb = (int)urlCacheMaxSizeUpDown.Value;
        try { _imageCache.Sweep(); }
        catch { }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void LogSavedSourceConfiguration(TagSourceCategory category, string? json)
    {
        if (_logger == null) return;
        var sources = TagSourceCatalog.Load(json, category, _settings.WebSearchItemsLimit);
        _logger.Debug("[标签源设置] 保存设置: 类别={0}, JSON={1}", category, json ?? "null");
        LogSourceConfiguration("保存设置解析后", category, sources);
    }

    private void LoadProxySourceSettings()
    {
        var json = _settings.ProxySourceSettings;
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var dict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, bool>>(json);
                if (dict != null)
                {
                    foreach (var kvp in proxySourceChecks)
                    {
                        kvp.Value.Checked = dict.TryGetValue(kvp.Key, out var enabled) && enabled;
                    }
                    return;
                }
            }
            catch { /* 忽略解析错误 */ }
        }
        foreach (var cb in proxySourceChecks.Values)
            cb.Checked = false;
    }

    private void SaveProxySourceSettings()
    {
        var dict = new Dictionary<string, bool>();
        foreach (var kvp in proxySourceChecks)
            dict[kvp.Key] = kvp.Value.Checked;
        _settings.ProxySourceSettings = Newtonsoft.Json.JsonConvert.SerializeObject(dict);
    }

    private static CheckBox CreateCheckBox(string text, int x, int y) =>
        new() { Text = text, Location = new Point(x, y), Width = 400, AutoSize = true };

    private static Label CreateLabel(string text, int x, int y, int width) =>
        new() { Text = text, Location = new Point(x, y), Width = width };

    private static TextBox CreateTextBox(string text, int x, int y, int width) =>
        new() { Text = text, Location = new Point(x, y), Width = width };

    private static Button CreateCacheButton(string text, int x, int y, int minimumWidth) =>
        new()
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(minimumWidth, 28),
            Padding = new Padding(6, 0, 6, 0),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.Black
        };

    private static ComboBox CreateCombo(string[] items, int x, int y)
    {
        var combo = new ComboBox
        {
            Location = new Point(x, y),
            Width = 120,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        combo.Items.AddRange(items);
        return combo;
    }
}
