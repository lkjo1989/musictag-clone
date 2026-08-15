using FontAwesome.Sharp;
using MusicTagClone.Controls;

namespace MusicTagClone.Forms;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null!;

    // === 菜单栏 ===
    private MenuStrip menuStrip;
    // 一级菜单
    private ToolStripMenuItem fileMenuItem;
    private ToolStripMenuItem editMenuItem;
    private ToolStripMenuItem viewMenuItem;
    private ToolStripMenuItem sourceMenuItem;
    private ToolStripMenuItem batchMenuItem;
    private ToolStripMenuItem toolsMenuItem;
    private ToolStripMenuItem helpMenuItem;

    // 文件(F) — 需单独置灰的项
    private ToolStripMenuItem saveTagsMenuItem;
    private ToolStripMenuItem clearTagsMenuItem;
    private ToolStripMenuItem readTagsMenuItem;
    private ToolStripMenuItem encodingFixMenuItem;
    private ToolStripMenuItem chsChtConvMenuItem;
    private ToolStripMenuItem tagHistoryMenuItem;

    // 编辑(E) — 需单独置灰的项
    private ToolStripMenuItem selectAllMenuItem;
    private ToolStripMenuItem deselectAllMenuItem;
    private ToolStripMenuItem invertSelectMenuItem;
    private ToolStripMenuItem renameMenuItem;
    private ToolStripMenuItem removeItemMenuItem;
    private ToolStripMenuItem deleteFileMenuItem;
    private ToolStripMenuItem openFileDirMenuItem;

    // 视图(V)
    private ToolStripMenuItem refreshMenuItem;
    private ToolStripMenuItem customizeColumnsMenuItem;

    // 标签源(S)

    // 批量(B) — 部分需单独置灰
    private ToolStripMenuItem autoMatchTagsMenuItem;

    // 语言(L)

    // === 工具栏（第二行：快捷图标栏） ===
    private ToolStrip toolStrip;
    private ToolStripButton changeWorkDirBtn;
    private ToolStripButton addDirBtn;
    private ToolStripButton manageDirBtn;
    private ToolStripSeparator toolSep1;
    private ToolStripButton saveTagsBtn;
    private ToolStripButton clearTagsBtn;
    private ToolStripButton undoBtn;
    private ToolStripButton readTagsBtn;
    private ToolStripButton encodingFixBtn;
    private ToolStripDropDownButton chsChtBtn;
    private ToolStripButton tagHistoryBtn;
    private ToolStripSeparator toolSep2;
    private ToolStripButton selectAllBtn;
    private ToolStripButton deselectAllBtn;
    private ToolStripSeparator toolSep3;
    private ToolStripButton refreshBtn;
    private ToolStripDropDownButton picSourceBtn;
    private ToolStripDropDownButton lrcSourceBtn;
    private ToolStripDropDownButton combSourceBtn;
    private ToolStripSeparator toolSep4;
    private ToolStripButton autoMatchBtn;
    private ToolStripButton saveLrcBtn;
    private ToolStripButton extractCoverBtn;
    private ToolStripButton filenameRelBtn;
    private ToolStripSeparator toolSep5;
    private ToolStripButton settingsBtn;

    // === 左侧：标签编辑面板 (含封面) ===
    private TagEditPanel tagEditPanel;

    // === 右侧：文件列表容器 ===
    private Panel rightPanel;

    // === 文件列表 ===
    private ListView fileListView;
    private ColumnHeader colFileName;
    private ColumnHeader colFileDir;
    private ColumnHeader colTagTypes;
    private ColumnHeader colTitle;
    private ColumnHeader colArtist;
    private ColumnHeader colAlbum;
    private ColumnHeader colAlbumArtist;
    private ColumnHeader colYear;
    private ColumnHeader colTrackStr;
    private ColumnHeader colDiscStr;
    private ColumnHeader colGenre;
    private ColumnHeader colComposer;
    private ColumnHeader colLyricist;
    private ColumnHeader colComment;
    private ColumnHeader colHasPicture;
    private ColumnHeader colLyrics;
    private ColumnHeader colChannels;
    private ColumnHeader colSampleRate;
    private ColumnHeader colBitRate;
    private ColumnHeader colBitPerSample;
    private ColumnHeader colDurationInMs;
    private ColumnHeader colUpdateTime;

    // === 底部面板 ===
    private Panel bottomPanel;
    private Label filterLabel;
    private TextBox filterTextBox;
    private ComboBox filterCombo;
    private Label statusLabel;
    private Label infoLabel;

    // === 底部进度条 ===
    private ProgressBar progressBar;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        // 颜色常量
        var bgColor = Color.FromArgb(245, 246, 248);
        var accentColor = Color.FromArgb(64, 158, 255);
        var toolbarBg = Color.FromArgb(255, 255, 255);
        var sidebarBg = Color.FromArgb(250, 251, 252);
        var bottomBg = Color.FromArgb(248, 249, 251);

        // ==========================================
        // === 菜单栏 ===
        // ==========================================
        menuStrip = new MenuStrip
        {
            BackColor = toolbarBg,
            Padding = new Padding(4, 2, 0, 2),
            Font = new Font("Microsoft YaHei UI", 9F),
            Renderer = new ToolStripProfessionalRenderer(new CleanColorTable())
        };

        // ==========================================
        // === 一、文件(F) ===
        // ==========================================
        fileMenuItem = new ToolStripMenuItem("文件(&F)");
        var changeWorkDirMI = NewMenuItem("改变工作目录...", OnChangeWorkDir, Keys.Control | Keys.D);
        var addDirMI = NewMenuItem("添加目录...", OnAddDirectory);
        var manageDirMI = NewMenuItem("管理目录...", OnManageDirectory);
        var fileSep1 = new ToolStripSeparator();
        saveTagsMenuItem = NewMenuItem("保存标签", OnSaveTags, Keys.Control | Keys.S);
        clearTagsMenuItem = NewMenuItem("清除标签", OnClearTags, Keys.Control | Keys.R);
        readTagsMenuItem = NewMenuItem("读取标签", OnReadTags, Keys.Control | Keys.T);
        encodingFixMenuItem = NewMenuItem("编码修正", OnEncodingFix);
        chsChtConvMenuItem = new ToolStripMenuItem("简繁转换(&C)");
        chsChtConvMenuItem.DropDownItems.AddRange(new ToolStripItem[] {
            NewMenuItem("标签简体→繁体", OnTagChsToCht),
            NewMenuItem("标签繁体→简体", OnTagChtToChs) });
        tagHistoryMenuItem = new ToolStripMenuItem("标签历史", null, OnTagHistory);
        // 标签历史子项动态生成，暂留空
        var fileSep2 = new ToolStripSeparator();
        var exitMenuItem = NewMenuItem("退出(&X)", OnExit);
        fileMenuItem.DropDownItems.AddRange(new ToolStripItem[] {
            changeWorkDirMI, addDirMI, manageDirMI,
            fileSep1,
            saveTagsMenuItem, clearTagsMenuItem, readTagsMenuItem,
            encodingFixMenuItem, chsChtConvMenuItem, tagHistoryMenuItem,
            fileSep2, exitMenuItem });

        // ==========================================
        // === 二、编辑(E) ===
        // ==========================================
        editMenuItem = new ToolStripMenuItem("编辑(&E)");
        selectAllMenuItem = NewMenuItem("全选", OnSelectAll, Keys.Control | Keys.A);
        deselectAllMenuItem = NewMenuItem("取消选定", OnDeselectAll, Keys.Control | Keys.U);
        invertSelectMenuItem = NewMenuItem("反选", OnInvertSelect, Keys.Control | Keys.Shift | Keys.A);
        var editSep1 = new ToolStripSeparator();
        renameMenuItem = NewMenuItem("重命名", OnRename, Keys.F2);
        removeItemMenuItem = NewMenuItem("删除项", OnRemoveFile, Keys.Delete);
        deleteFileMenuItem = NewMenuItem("删除文件", OnDeleteFiles, Keys.Shift | Keys.Delete);
        openFileDirMenuItem = NewMenuItem("打开文件目录", OnOpenFileDirectory);
        editMenuItem.DropDownItems.AddRange(new ToolStripItem[] {
            selectAllMenuItem, deselectAllMenuItem, invertSelectMenuItem,
            editSep1,
            renameMenuItem, removeItemMenuItem,
            deleteFileMenuItem, openFileDirMenuItem });

        // ==========================================
        // === 三、视图(V) ===
        // ==========================================
        viewMenuItem = new ToolStripMenuItem("视图(&V)");
        refreshMenuItem = NewMenuItem("刷新", OnRefresh, Keys.F5);
        customizeColumnsMenuItem = NewMenuItem("自定义显示列...", OnCustomizeColumns);
        viewMenuItem.DropDownItems.AddRange(new ToolStripItem[] {
            refreshMenuItem, customizeColumnsMenuItem });

        // ==========================================
        // === 四、标签源(S) ===
        // ==========================================
        sourceMenuItem = new ToolStripMenuItem("标签源(&S)");
        var picSourceMI = new ToolStripMenuItem("图片");
        picSourceMI.DropDownItems.AddRange(new ToolStripItem[] {
            NewMenuItem("网易云", (s, e) => OnSearchPictureSource("netease")),
            NewMenuItem("QQ", (s, e) => OnSearchPictureSource("qq")),
            NewMenuItem("iTunes", (s, e) => OnSearchPictureSource("itunes")),
            NewMenuItem("酷我", (s, e) => OnSearchPictureSource("kuwo")),
            NewMenuItem("Last.fm", (s, e) => OnSearchPictureSource("lastfm")),
            NewMenuItem("MusicBrainz", (s, e) => OnSearchPictureSource("musicbrainz")),
            NewMenuItem("Discogs", (s, e) => OnSearchPictureSource("discogs")) });
        var lrcSourceMI = new ToolStripMenuItem("歌词");
        lrcSourceMI.DropDownItems.AddRange(new ToolStripItem[] {
            NewMenuItem("网易云", (s, e) => OnSearchLyricSource("netease")),
            NewMenuItem("QQ", (s, e) => OnSearchLyricSource("qq")),
            NewMenuItem("酷我", (s, e) => OnSearchLyricSource("kuwo")),
            NewMenuItem("酷狗", (s, e) => OnSearchLyricSource("kugou")) });
        var combSourceMI = new ToolStripMenuItem("组合标签");
        combSourceMI.DropDownItems.AddRange(new ToolStripItem[] {
            NewMenuItem("网易云", (s, e) => OnSearchCombTagSource("netease")),
            NewMenuItem("QQ", (s, e) => OnSearchCombTagSource("qq")),
            NewMenuItem("iTunes", (s, e) => OnSearchCombTagSource("itunes")),
            NewMenuItem("酷我", (s, e) => OnSearchCombTagSource("kuwo")),
            NewMenuItem("Last.fm", (s, e) => OnSearchCombTagSource("lastfm")),
            NewMenuItem("MusicBrainz", (s, e) => OnSearchCombTagSource("musicbrainz")),
            NewMenuItem("Discogs", (s, e) => OnSearchCombTagSource("discogs")) });
        sourceMenuItem.DropDownItems.AddRange(new ToolStripItem[] {
            picSourceMI, lrcSourceMI, combSourceMI });

        // ==========================================
        // === 五、批量(B) ===
        // ==========================================
        batchMenuItem = new ToolStripMenuItem("批量(&B)");
        autoMatchTagsMenuItem = NewMenuItem("自动匹配标签...", OnBatchAutoMatch);
        var batchLyricMI = new ToolStripMenuItem("歌词");
        batchLyricMI.DropDownItems.AddRange(new ToolStripItem[] {
            NewMenuItem("格式化歌词时间轴", OnFormatLyricTimeline),
            NewMenuItem("删除歌词时间轴", OnRemoveLyricTimeline),
            NewMenuItem("删除歌词空白文字的行", OnDeleteLyricBlankLines),
            NewMenuItem("删除歌词头部标签", OnDeleteLyricHeadTag),
            new ToolStripSeparator(),
            NewMenuItem("另存歌词为 lrc 文件...", OnSaveLyricAsLrc),
            NewMenuItem("导入 lrc 文件到歌词...", OnImportLrcToLyric) });
        var extractCoverMI = NewMenuItem("提取封面...", OnExtractCover);
        var batchFilenameMI = NewMenuItem("文件名相关...", OnBatchFilenameRel);
        var batchChsChtMI = new ToolStripMenuItem("简繁转换");
        batchChsChtMI.DropDownItems.AddRange(new ToolStripItem[] {
            NewMenuItem("标签繁体→简体", OnBatchTagChtToChs),
            NewMenuItem("标签简体→繁体", OnBatchTagChsToCht),
            new ToolStripSeparator(),
            NewMenuItem("文件名繁体→简体", OnBatchFilenameChtToChs),
            NewMenuItem("文件名简体→繁体", OnBatchFilenameChsToCht) });
        batchMenuItem.DropDownItems.AddRange(new ToolStripItem[] {
            autoMatchTagsMenuItem, batchLyricMI, extractCoverMI,
            batchFilenameMI, batchChsChtMI });

        // ==========================================
        // === 六、工具(T) ===
        // ==========================================
        toolsMenuItem = new ToolStripMenuItem("工具(&T)");
        var optionsMI = NewMenuItem("设置", OnSettings);
        toolsMenuItem.DropDownItems.AddRange(new ToolStripItem[] { optionsMI });
        // ==========================================
        // === 八、帮助(H) ===
        // ==========================================
        helpMenuItem = new ToolStripMenuItem("帮助(&H)");
        var checkUpdateMI = NewMenuItem("检查新版本", OnCheckUpdate);
        var officialSiteMI = NewMenuItem("官方网站", OnOfficialSite);
        var helpSep = new ToolStripSeparator();
        var aboutMI = NewMenuItem("关于(&A)", OnAbout);
        helpMenuItem.DropDownItems.AddRange(new ToolStripItem[] {
            checkUpdateMI, officialSiteMI, helpSep, aboutMI });

        // ====== 注册到菜单栏 ======
        menuStrip.Items.AddRange(new ToolStripItem[] {
            fileMenuItem, editMenuItem, viewMenuItem, sourceMenuItem,
            batchMenuItem, toolsMenuItem, helpMenuItem });

        // ====== 菜单置灰: 下拉前刷新 ======
        fileMenuItem.DropDownOpening += (s, e) => RefreshMenuStates();
        editMenuItem.DropDownOpening += (s, e) => RefreshMenuStates();
        sourceMenuItem.DropDownOpening += (s, e) => RefreshMenuStates();
        batchMenuItem.DropDownOpening += (s, e) => RefreshMenuStates();

        // ==========================================
        // === 工具栏 ===
        // ==========================================
        toolStrip = new ToolStrip
        {
            BackColor = toolbarBg,
            GripStyle = ToolStripGripStyle.Hidden,
            Renderer = new ToolStripProfessionalRenderer(new CleanColorTable()),
            Padding = new Padding(4, 2, 4, 2),
            Font = new Font("Microsoft YaHei UI", 9F)
        };

        // 快捷图标栏 — 纯图标、不显示文字，ToolTip 提示；复用菜单/既有事件处理器，不重复实现逻辑
        changeWorkDirBtn = CreateIconBtn(IconChar.FolderOpen, "改变工作目录", OnChangeWorkDir);
        addDirBtn = CreateIconBtn(IconChar.FolderPlus, "添加目录", OnOpenDirectory);
        manageDirBtn = CreateIconBtn(IconChar.Folder, "管理目录", OnManageDirectory);
        toolSep1 = new ToolStripSeparator();
        saveTagsBtn = CreateIconBtn(IconChar.Save, "保存标签", OnSaveTags);
        clearTagsBtn = CreateIconBtn(IconChar.Eraser, "清除标签", OnClearTags);
        undoBtn = CreateIconBtn(IconChar.Undo, "撤销", OnDiscardChanges);
        readTagsBtn = CreateIconBtn(IconChar.Tags, "读取标签", OnReadTags);
        encodingFixBtn = CreateIconBtn(IconChar.Wrench, "编码修正", OnEncodingFix);
        chsChtBtn = CreateIconDropBtn(IconChar.ExchangeAlt, "简繁转换");
        chsChtBtn.DropDownItems.AddRange(new ToolStripItem[] {
            NewMenuItem("标签简体→繁体", OnTagChsToCht),
            NewMenuItem("标签繁体→简体", OnTagChtToChs) });
        tagHistoryBtn = CreateIconBtn(IconChar.History, "标签历史", OnTagHistory);
        toolSep2 = new ToolStripSeparator();
        selectAllBtn = CreateIconBtn(IconChar.CheckDouble, "全选", OnSelectAll);
        deselectAllBtn = CreateIconBtn(IconChar.Square, "取消选定", OnDeselectAll);
        toolSep3 = new ToolStripSeparator();
        refreshBtn = CreateIconBtn(IconChar.SyncAlt, "刷新", OnRefresh);
        picSourceBtn = CreateIconDropBtn(IconChar.Image, "图片源");
        picSourceBtn.DropDownItems.AddRange(new ToolStripItem[] {
            NewMenuItem("网易云", (s, e) => OnSearchPictureSource("netease")),
            NewMenuItem("QQ", (s, e) => OnSearchPictureSource("qq")),
            NewMenuItem("iTunes", (s, e) => OnSearchPictureSource("itunes")),
            NewMenuItem("酷我", (s, e) => OnSearchPictureSource("kuwo")),
            NewMenuItem("Last.fm", (s, e) => OnSearchPictureSource("lastfm")),
            NewMenuItem("MusicBrainz", (s, e) => OnSearchPictureSource("musicbrainz")),
            NewMenuItem("Discogs", (s, e) => OnSearchPictureSource("discogs")) });
        lrcSourceBtn = CreateIconDropBtn(IconChar.Music, "歌词源");
        lrcSourceBtn.DropDownItems.AddRange(new ToolStripItem[] {
            NewMenuItem("网易云", (s, e) => OnSearchLyricSource("netease")),
            NewMenuItem("QQ", (s, e) => OnSearchLyricSource("qq")),
            NewMenuItem("酷我", (s, e) => OnSearchLyricSource("kuwo")),
            NewMenuItem("酷狗", (s, e) => OnSearchLyricSource("kugou")) });
        combSourceBtn = CreateIconDropBtn(IconChar.CloudDownloadAlt, "组合标签源");
        combSourceBtn.DropDownItems.AddRange(new ToolStripItem[] {
            NewMenuItem("网易云", (s, e) => OnSearchCombTagSource("netease")),
            NewMenuItem("QQ", (s, e) => OnSearchCombTagSource("qq")),
            NewMenuItem("iTunes", (s, e) => OnSearchCombTagSource("itunes")),
            NewMenuItem("酷我", (s, e) => OnSearchCombTagSource("kuwo")),
            NewMenuItem("Last.fm", (s, e) => OnSearchCombTagSource("lastfm")),
            NewMenuItem("MusicBrainz", (s, e) => OnSearchCombTagSource("musicbrainz")),
            NewMenuItem("Discogs", (s, e) => OnSearchCombTagSource("discogs")) });
        toolSep4 = new ToolStripSeparator();
        autoMatchBtn = CreateIconBtn(IconChar.Magic, "自动匹配标签", OnBatchAutoMatch);
        saveLrcBtn = CreateIconBtn(IconChar.FileAlt, "另存歌词为LRC", OnSaveLyricAsLrc);
        extractCoverBtn = CreateIconBtn(IconChar.FileImage, "提取封面", OnExtractCover);
        filenameRelBtn = CreateIconBtn(IconChar.FileSignature, "文件名相关", OnBatchFilenameRel);
        toolSep5 = new ToolStripSeparator();
        settingsBtn = CreateIconBtn(IconChar.Cog, "设置", OnSettings);

        toolStrip.Items.AddRange(new ToolStripItem[] {
            changeWorkDirBtn, addDirBtn, manageDirBtn,
            toolSep1,
            saveTagsBtn, clearTagsBtn, undoBtn, readTagsBtn,
            encodingFixBtn, chsChtBtn, tagHistoryBtn,
            toolSep2,
            selectAllBtn, deselectAllBtn,
            toolSep3,
            refreshBtn, picSourceBtn, lrcSourceBtn, combSourceBtn,
            toolSep4,
            autoMatchBtn, saveLrcBtn, extractCoverBtn, filenameRelBtn,
            toolSep5,
            settingsBtn });

        // ==========================================
        // === 左侧：标签编辑面板 ===
        // ==========================================
        tagEditPanel = new TagEditPanel
        {
            Dock = DockStyle.Fill,
            BackColor = sidebarBg
        };

        // ==========================================
        // === 右侧：文件列表 ===
        // ==========================================
        fileListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            CheckBoxes = false,
            MultiSelect = true,
            AllowColumnReorder = true,
            LabelEdit = true,
            Font = new Font("Microsoft YaHei UI", 9F),
            BackColor = Color.White,
            GridLines = true,
            HeaderStyle = ColumnHeaderStyle.Clickable,
            HideSelection = false,
        };
        // 启用双缓冲减少闪烁
        typeof(ListView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(fileListView, true);
        colFileName = new ColumnHeader { Text = "文件名", Width = 200 };
        colFileDir = new ColumnHeader { Text = "目录", Width = 160 };
        colTagTypes = new ColumnHeader { Text = "标签格式", Width = 80 };
        colTitle = new ColumnHeader { Text = "标题", Width = 150 };
        colArtist = new ColumnHeader { Text = "艺术家", Width = 120 };
        colAlbum = new ColumnHeader { Text = "专辑", Width = 120 };
        colAlbumArtist = new ColumnHeader { Text = "专辑艺术家", Width = 100 };
        colYear = new ColumnHeader { Text = "年份", Width = 60, TextAlign = HorizontalAlignment.Right };
        colTrackStr = new ColumnHeader { Text = "音轨号", Width = 60, TextAlign = HorizontalAlignment.Right };
        colDiscStr = new ColumnHeader { Text = "碟号", Width = 50, TextAlign = HorizontalAlignment.Right };
        colGenre = new ColumnHeader { Text = "风格", Width = 80 };
        colComposer = new ColumnHeader { Text = "作曲家", Width = 100 };
        colLyricist = new ColumnHeader { Text = "作词家", Width = 100 };
        colComment = new ColumnHeader { Text = "注释", Width = 100 };
        colHasPicture = new ColumnHeader { Text = "封面", Width = 50, TextAlign = HorizontalAlignment.Center };
        colLyrics = new ColumnHeader { Text = "歌词", Width = 60 };
        colChannels = new ColumnHeader { Text = "声道", Width = 50, TextAlign = HorizontalAlignment.Right };
        colSampleRate = new ColumnHeader { Text = "采样率", Width = 70, TextAlign = HorizontalAlignment.Right };
        colBitRate = new ColumnHeader { Text = "比特率", Width = 70, TextAlign = HorizontalAlignment.Right };
        colBitPerSample = new ColumnHeader { Text = "位深", Width = 60, TextAlign = HorizontalAlignment.Right };
        colDurationInMs = new ColumnHeader { Text = "时长", Width = 70, TextAlign = HorizontalAlignment.Right };
        colUpdateTime = new ColumnHeader { Text = "修改时间", Width = 120 };
        fileListView.Columns.AddRange(new ColumnHeader[] {
            colFileName, colFileDir, colTagTypes, colTitle, colArtist, colAlbum,
            colAlbumArtist, colYear, colTrackStr, colDiscStr, colGenre,
            colComposer, colLyricist, colComment, colHasPicture, colLyrics,
            colChannels, colSampleRate, colBitRate, colBitPerSample, colDurationInMs, colUpdateTime
        });
        fileListView.SelectedIndexChanged += OnFileListViewSelectedIndexChanged;
        fileListView.ColumnClick += OnFileListViewColumnClick;
        fileListView.MouseDown += OnFileListViewMouseDown;
        fileListView.AfterLabelEdit += OnFileAfterLabelEdit;
        fileListView.ColumnReordered += OnFileListViewColumnReordered;
        fileListView.ColumnWidthChanged += OnFileListViewColumnWidthChanged;

        // ==========================================
        // === 底部面板: 过滤 + 状态 ===
        // ==========================================
        bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 32,
            BackColor = bottomBg,
            Padding = new Padding(8, 5, 8, 4)
        };

        filterLabel = new Label
        {
            Text = "过滤:",
            Location = new Point(8, 6),
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.FromArgb(96, 98, 102)
        };

        filterTextBox = new TextBox
        {
            Location = new Point(50, 4),
            Width = 150,
            Font = new Font("Microsoft YaHei UI", 9F),
            BorderStyle = BorderStyle.FixedSingle
        };
        filterTextBox.TextChanged += OnFilterTextChanged;

        filterCombo = new ComboBox
        {
            Location = new Point(205, 4),
            Width = 65,
            Font = new Font("Microsoft YaHei UI", 9F),
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat
        };
        filterCombo.Items.AddRange(new object[] { "任意", "MP3", "FLAC", "M4A", "OGG", "WMA", "WAV", "APE" });
        filterCombo.SelectedIndex = 0;
        filterCombo.SelectedIndexChanged += (s, e) => ApplyFilter(filterCombo.SelectedIndex > 0
            ? $".{filterCombo.SelectedItem!.ToString()!.ToLowerInvariant()}" : "");

        // 中间状态文字
        statusLabel = new Label
        {
            Text = "",
            Location = new Point(285, 6),
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.FromArgb(120, 122, 128),
            TextAlign = ContentAlignment.MiddleLeft
        };

        // 右下角文件数+时长+体积
        infoLabel = new Label
        {
            Text = "0 (00:00:00 | 0 Byte)",
            Location = new Point(0, 6),
            AutoSize = true,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.FromArgb(120, 122, 128),
            TextAlign = ContentAlignment.MiddleRight
        };
        infoLabel.Dock = DockStyle.Right;

        // 进度条（叠加在中间区域）
        progressBar = new ProgressBar
        {
            Location = new Point(285, 5),
            Width = 180,
            Height = 14,
            Visible = false,
            Style = ProgressBarStyle.Continuous
        };

        bottomPanel.Controls.AddRange(new Control[] {
            filterLabel, filterTextBox, filterCombo, statusLabel, progressBar, infoLabel });

        // ==========================================
        // === 主布局: LEFT=tag编辑(固定宽), RIGHT=文件列表+底部栏 ===
        // ==========================================
        rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };

        tagEditPanel.Width = 325;
        tagEditPanel.Dock = DockStyle.Left;

        // 底部栏放进右侧容器里,和文件列表左对齐
        fileListView.Dock = DockStyle.Fill;
        rightPanel.Controls.Add(fileListView);
        rightPanel.Controls.Add(bottomPanel);

        // 组装
        Controls.Add(rightPanel);
        Controls.Add(tagEditPanel); // Dock.Left 在 Dock.Fill 之上

        // ==========================================
        // === 事件绑定 ===
        // ==========================================
        tagEditPanel.LyricsEditRequested += OnLyricsEdit;
        tagEditPanel.EncodingFixRequested += OnEncodingFixForField;

        tagEditPanel.CoverOpenRequested += OnOpenCover;
        tagEditPanel.CoverDeleteRequested += OnDeleteCover;
        tagEditPanel.CoverExtractRequested += OnExtractCover;
        tagEditPanel.CoverCompressRequested += OnCompressCover;
        tagEditPanel.CoverOpenExternalRequested += OnOpenCoverExternal;
        tagEditPanel.CoverTypeChanged += OnCoverTypeChanged;
        tagEditPanel.CoverIndexChanged += OnCoverIndexChanged;

        // ==========================================
        // === 主窗口 ===
        // ==========================================
        Text = "MusicTag Clone - 音乐标签管理工具";
        Size = new Size(1400, 920);
        MinimumSize = new Size(1000, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = bgColor;
        Font = new Font("Microsoft YaHei UI", 9F);

        // 设置程序图标（从 EXE 自身提取，由 ApplicationIcon 编译嵌入）
        try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        catch { }

        Controls.Add(toolStrip);
        Controls.Add(menuStrip);
        MainMenuStrip = menuStrip;

        // 初始置灰（须在工具栏按钮创建之后调用）
        SetInitialMenuState();

        FormClosing += OnMainFormClosing;
    }

    /// <summary>创建快捷图标按钮（纯图标、无文字，悬停显示 ToolTip）</summary>
    private static ToolStripButton CreateIconBtn(IconChar icon, string tooltip, EventHandler handler)
    {
        var btn = new ToolStripButton
        {
            Image = IconHelper.GetToolIcon(icon),
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            ToolTipText = tooltip,
            Font = new Font("Microsoft YaHei UI", 9F)
        };
        btn.Click += handler;
        return btn;
    }

    /// <summary>创建快捷图标下拉按钮（纯图标、无文字，点击展开子项）</summary>
    private static ToolStripDropDownButton CreateIconDropBtn(IconChar icon, string tooltip) => new()
    {
        Image = IconHelper.GetToolIcon(icon),
        DisplayStyle = ToolStripItemDisplayStyle.Image,
        ToolTipText = tooltip,
        Font = new Font("Microsoft YaHei UI", 9F)
    };

    /// <summary>创建菜单项快捷方法</summary>
    private static ToolStripMenuItem NewMenuItem(string text, EventHandler? handler = null, Keys shortcut = Keys.None) =>
        new(text, null, handler) { ShortcutKeys = shortcut };

    /// <summary>初始化菜单置灰状态（程序启动时调用）</summary>
    private void SetInitialMenuState()
    {
        // 文件菜单: 保存/清除/读取/编码/简繁/标签历史 初始置灰
        for (int i = 4; i <= 9; i++)
            fileMenuItem.DropDownItems[i].Enabled = false;
        // 编辑菜单
        selectAllMenuItem.Enabled = false;
        deselectAllMenuItem.Enabled = false;
        invertSelectMenuItem.Enabled = false;
        for (int i = 4; i <= 7; i++)
            editMenuItem.DropDownItems[i].Enabled = false;
        // 标签源 / 批量 整菜单置灰
        sourceMenuItem.Enabled = false;
        batchMenuItem.Enabled = false;
        // 工具栏图片/歌词/组合标签源下拉按钮：未选文件时置灰
        picSourceBtn.Enabled = false;
        lrcSourceBtn.Enabled = false;
        combSourceBtn.Enabled = false;
        // 工具栏批量组按钮：初始置灰
        autoMatchBtn.Enabled = false;
        saveLrcBtn.Enabled = false;
        extractCoverBtn.Enabled = false;
        filenameRelBtn.Enabled = false;
        // 工具栏第二组（标签相关）按钮：未选文件时置灰
        saveTagsBtn.Enabled = false;
        clearTagsBtn.Enabled = false;
        undoBtn.Enabled = false;
        readTagsBtn.Enabled = false;
        encodingFixBtn.Enabled = false;
        chsChtBtn.Enabled = false;
        tagHistoryBtn.Enabled = false;
    }
}

/// <summary>
/// 工具栏颜色方案
/// </summary>
internal class CleanColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Color.White;
    public override Color MenuBorder => Color.FromArgb(220, 222, 226);
    public override Color MenuItemBorder => Color.FromArgb(64, 158, 255);
    public override Color MenuItemSelected => Color.FromArgb(236, 245, 255);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(236, 245, 255);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(236, 245, 255);
    public override Color MenuItemPressedGradientBegin => Color.FromArgb(236, 245, 255);
    public override Color MenuItemPressedGradientEnd => Color.FromArgb(236, 245, 255);
    public override Color MenuStripGradientBegin => Color.White;
    public override Color MenuStripGradientEnd => Color.White;
    public override Color SeparatorDark => Color.FromArgb(220, 222, 226);
    public override Color SeparatorLight => Color.White;
    public override Color ToolStripGradientBegin => Color.White;
    public override Color ToolStripGradientEnd => Color.White;
    public override Color ToolStripGradientMiddle => Color.White;
}
