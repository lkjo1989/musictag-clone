using System.ComponentModel;
using MusicTagClone.Models;

namespace MusicTagClone.Controls;

/// <summary>
/// 标签编辑面板 — 左侧边栏，从上到下包含：标题、艺术家、专辑、年份(带工具按钮)、
/// 音轨号/碟号(并排)、风格、专辑艺术家、作曲家、作词家、注释、
/// 歌词(带编辑按钮)、封面预览(带覆盖选项)。
/// 每行文本字段右侧均有编码修正按钮。
/// </summary>
public partial class TagEditPanel : UserControl
{
    // 标签字段（下拉文本框）
    private Label titleLabel;
    private ComboBox titleBox;
    private Button titleEncBtn;
    private Label artistLabel;
    private ComboBox artistBox;
    private Button artistEncBtn;
    private Label albumLabel;
    private ComboBox albumBox;
    private Button albumEncBtn;
    private Label yearLabel;
    private ComboBox yearBox;
    private Button yearEncBtn;
    private Label trackLabel;
    private ComboBox trackNumeric;
    private Label discLabel;
    private ComboBox discNumeric;
    private Label genreLabel;
    private ComboBox genreBox;
    private Button genreEncBtn;
    private Label albumArtistLabel;
    private ComboBox albumArtistBox;
    private Button albumArtistEncBtn;
    private Label composerLabel;
    private ComboBox composerBox;
    private Button composerEncBtn;
    private Label lyricistLabel;
    private ComboBox lyricistBox;
    private Button lyricistEncBtn;
    private Label commentLabel;
    private ComboBox commentBox;
    private Button commentEncBtn;

    // 歌词行
    private Label lyricLabel;
    private ComboBox lyricPreviewBox;
    private Button lyricEncBtn;
    private Button lyricEditBtn;

    // 封面区
    private Label coverSectionLabel;
    private PictureBox coverPictureBox;
    private Label coverFormatLabel;
    private Label coverResolutionLabel;
    private Label coverSizeLabel;
    private Label coverTypeLabel;
    private Panel coverNavPanel;
    private Button coverPrevBtn;
    private Label coverIndexLabel;
    private Button coverNextBtn;
    private CheckBox overwriteCoverCheck;

    // 多图支持
    private List<CoverArt>? _pictures;
    private int _currentPictureIndex;

    private MusicFile? _currentFile;
    private ToolTip _toolTip = null!;

    /// <summary>字段名 → (标签控件, 原始文本)，用于修改后显示"(已修改)"</summary>
    private readonly Dictionary<string, (Label Label, string OriginalText)> _fieldLabels = new();

    /// <summary>正在加载文件，抑制修改标记</summary>
    private bool _loading;

    // 事件：外部订阅
    public event EventHandler? LyricsEditRequested;
    /// <summary>编码修正请求，e.Tag 为字段名（如 "title"、"artist"）</summary>
    public event EventHandler<EncodingFixEventArgs>? EncodingFixRequested;

    // 封面操作事件
    public event EventHandler? CoverOpenRequested;
    public event EventHandler? CoverDeleteRequested;
    public event EventHandler? CoverExtractRequested;
    public event EventHandler? CoverOpenExternalRequested;
    public event EventHandler? CoverCompressRequested;
    /// <summary>封面类型更改请求，e 为 TagLib PictureType 枚举值</summary>
    public event EventHandler<CoverPictureType>? CoverTypeChanged;
    /// <summary>图片索引切换事件（上一张/下一张）</summary>
    public event EventHandler? CoverIndexChanged;

    // 颜色常量
    private const int IconBtnWidth = 22;
    private static readonly Color LabelColor = Color.Black;
    private static readonly Color InputBg = Color.White;
    private static readonly Color SeparatorColor = Color.FromArgb(220, 223, 230);
    private static readonly Color CoverBg = Color.FromArgb(248, 249, 250);

    public TagEditPanel()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        AutoScroll = true;
        Padding = new Padding(12, 10, 12, 12);
        BackColor = Color.FromArgb(250, 251, 252);

        // ====== 统一网格参数 ======
        const int margin = 20;          // 控件左缩进
        const int inputW = 254;         // 文本框宽度（195 的 1.3 倍）
        const int btnW = IconBtnWidth;  // 编码修正按钮宽
        const int rowH = 50;            // 每行总高(标签+文本框+间距)
        const int labelH = 17;          // 标签高度
        const int inputH = 23;          // 文本框高度
        // 文本框 X = margin, 编码按钮 X = margin + inputW + 4
        const int btnX = margin + inputW + 4;
        // 面板宽度 325px (padding 12×2=24, 内容区 301px)

        var y = 5;

        // ====== 字段辅助：标签在上，下拉文本框+按钮在下 ======
        void AddField(string label, string fieldName, out Label lbl, out ComboBox box, out Button btn, int x = margin)
        {
            lbl = new Label
            {
                Text = label,
                Location = new Point(x, y),
                AutoSize = true,
                Height = labelH,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = LabelColor
            };
            box = new ComboBox
            {
                Location = new Point(x, y + labelH + 1),
                Width = inputW,
                Height = inputH,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                FlatStyle = FlatStyle.Standard,
                BackColor = InputBg,
                DropDownStyle = ComboBoxStyle.DropDown,
                DropDownHeight = 150,
            };
            box.TextUpdate += OnFieldChanged;
            box.TextChanged += OnFieldChanged;
            box.Tag = fieldName;
            btn = CreateEncBtn(btnX, y + labelH + 1, fieldName);
            if (!string.IsNullOrEmpty(fieldName))
                RegisterFieldLabel(fieldName, lbl);
            y += rowH;
        }

        // ====== 字段列表 ======
        AddField("标题", "title", out titleLabel, out titleBox, out titleEncBtn);
        AddField("艺术家", "artist", out artistLabel, out artistBox, out artistEncBtn);
        AddField("专辑", "album", out albumLabel, out albumBox, out albumEncBtn);

        // 年份（下拉文本框，支持 &lt;keep&gt;/&lt;blank&gt;）
        yearLabel = new Label
        {
            Text = "年份",
            Location = new Point(margin, y),
            AutoSize = true,
            Height = labelH,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = LabelColor
        };
        yearBox = new ComboBox
        {
            Location = new Point(margin, y + labelH + 1),
            Width = inputW,
            Height = inputH,
            Font = new Font("Microsoft YaHei UI", 9.5F),
            FlatStyle = FlatStyle.Standard,
            BackColor = InputBg,
            DropDownStyle = ComboBoxStyle.DropDown,
            DropDownHeight = 150,
        };
        yearBox.TextUpdate += OnFieldChanged;
        yearBox.TextChanged += OnFieldChanged;
        yearBox.Tag = "year";
        yearEncBtn = CreateEncBtn(btnX, y + labelH + 1, "year");
        RegisterFieldLabel("year", yearLabel);
        y += rowH;

        // 音轨号 / 碟号（并排，共用一行）
        trackLabel = new Label
        {
            Text = "音轨号",
            Location = new Point(margin, y),
            AutoSize = true,
            Height = labelH,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = LabelColor
        };
        discLabel = new Label
        {
            Text = "碟号",
            Location = new Point(margin + 104, y),
            AutoSize = true,
            Height = labelH,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = LabelColor
        };
        trackNumeric = new ComboBox
        {
            Location = new Point(margin, y + labelH + 1),
            Width = 80,
            Height = inputH,
            Font = new Font("Microsoft YaHei UI", 9.5F),
            FlatStyle = FlatStyle.Standard,
            BackColor = InputBg,
            DropDownStyle = ComboBoxStyle.DropDown,
            DropDownHeight = 150,
        };
        trackNumeric.Tag = "track";
        discNumeric = new ComboBox
        {
            Location = new Point(margin + 104, y + labelH + 1),
            Width = inputW - 104,
            Height = inputH,
            Font = new Font("Microsoft YaHei UI", 9.5F),
            FlatStyle = FlatStyle.Standard,
            BackColor = InputBg,
            DropDownStyle = ComboBoxStyle.DropDown,
            DropDownHeight = 150,
        };
        discNumeric.Tag = "disc";
        trackNumeric.TextUpdate += OnFieldChanged;
        discNumeric.TextUpdate += OnFieldChanged;
        trackNumeric.TextChanged += OnFieldChanged;
        discNumeric.TextChanged += OnFieldChanged;
        // 音轨号和碟号没有编码修正按钮
        RegisterFieldLabel("track", trackLabel);
        RegisterFieldLabel("disc", discLabel);
        y += rowH;

        AddField("风格", "genre", out genreLabel, out genreBox, out genreEncBtn);
        AddField("专辑艺术家", "albumartist", out albumArtistLabel, out albumArtistBox, out albumArtistEncBtn);
        AddField("作曲家", "composer", out composerLabel, out composerBox, out composerEncBtn);
        AddField("作词家", "lyricist", out lyricistLabel, out lyricistBox, out lyricistEncBtn);
        AddField("注释", "comment", out commentLabel, out commentBox, out commentEncBtn);

        // ====== 歌词 ======
        lyricLabel = new Label
        {
            Text = "歌词",
            Location = new Point(margin, y),
            AutoSize = true,
            Height = labelH,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = LabelColor
        };
        lyricPreviewBox = new ComboBox
        {
            Location = new Point(margin, y + labelH + 1),
            Width = inputW - btnW * 2 - 8,
            Height = inputH,
            Font = new Font("Microsoft YaHei UI", 9F),
            FlatStyle = FlatStyle.Standard,
            BackColor = Color.White,
            DropDownStyle = ComboBoxStyle.DropDown,
            DropDownHeight = 150,
        };
        lyricEditBtn = new Button
        {
            Size = new Size(btnW, inputH),
            Location = new Point(btnX - btnW - 4, y + labelH + 1),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = LabelColor,
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleCenter,
            ImageAlign = ContentAlignment.MiddleCenter
        };
        lyricEditBtn.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        lyricEditBtn.FlatAppearance.BorderSize = 1;
        lyricEditBtn.Click += (s, e) => LyricsEditRequested?.Invoke(this, EventArgs.Empty);
        lyricEditBtn.Image = IconHelper.GetLyricIcon();
        lyricEncBtn = CreateEncBtn(btnX, y + labelH + 1, "lyrics");
        RegisterFieldLabel("lyrics", lyricLabel);
        y += rowH;

        // ====== 分隔线 ======
        y += 6;
        var separator = new Label
        {
            AutoSize = false,
            Height = 1,
            Width = btnX + btnW,
            Location = new Point(margin, y),
            BackColor = SeparatorColor
        };
        y += 14;

        // ====== 封面区 ======
        coverSectionLabel = new Label
        {
            Text = "图片",
            Location = new Point(margin, y),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            ForeColor = LabelColor,
            AutoSize = true
        };
        y += 22;

        // 面板宽度 315, padding 12×2, 内容区 291
        const int coverSize = 170;
        var coverPanel = new Panel
        {
            Location = new Point(margin, y),
            Size = new Size(coverSize, coverSize),
            BackColor = CoverBg,
            BorderStyle = BorderStyle.FixedSingle
        };

        coverPictureBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = CoverBg
        };
        coverPictureBox.Paint += (s, e) =>
        {
            if (coverPictureBox.Image == null)
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var cx = coverPictureBox.Width / 2;
                var cy = coverPictureBox.Height / 2;
                var r = Math.Min(cx, cy) - 18;

                using var outer = new Pen(Color.FromArgb(200, 200, 200), 2);
                g.DrawEllipse(outer, cx - r, cy - r, r * 2, r * 2);
                using var inner = new SolidBrush(Color.FromArgb(220, 220, 220));
                g.FillEllipse(inner, cx - r + 3, cy - r + 3, (r - 3) * 2, (r - 3) * 2);
                using var hole = new SolidBrush(CoverBg);
                g.FillEllipse(hole, cx - 16, cy - 16, 32, 32);
                using var holePen = new Pen(Color.FromArgb(180, 180, 180), 1.5f);
                g.DrawEllipse(holePen, cx - 16, cy - 16, 32, 32);
            }
        };

        // 封面右侧信息（面板宽 325, padding 12×2, 内容区 301）
        int infoX = margin + coverSize + 8;
        int infoW = 301 - infoX;
        var infoFont = new Font("Microsoft YaHei UI", 8.25F);

        coverFormatLabel = new Label
        {
            Text = "--",
            Location = new Point(infoX, y + 4),
            Width = infoW,
            AutoSize = false,
            Font = infoFont,
            ForeColor = LabelColor
        };
        coverResolutionLabel = new Label
        {
            Text = "--",
            Location = new Point(infoX, y + 26),
            Width = infoW,
            AutoSize = false,
            Font = infoFont,
            ForeColor = LabelColor
        };
        coverSizeLabel = new Label
        {
            Text = "--",
            Location = new Point(infoX, y + 48),
            Width = infoW,
            AutoSize = false,
            Font = infoFont,
            ForeColor = LabelColor
        };
        coverTypeLabel = new Label
        {
            Text = "--",
            Location = new Point(infoX, y + 68),
            Width = infoW,
            AutoSize = false,
            Font = infoFont,
            ForeColor = LabelColor
        };

        // 图片导航（多图时显示，置于封面下方居中）
        var navFont = new Font("Microsoft YaHei UI", 9F);
        int navBtnW = 22, navBtnH = 20;
        int navRowH = 24;   // 导航行高度（封面下方预留）
        int indexW = 40;
        int navTotalW = navBtnW + 2 + indexW + 2 + navBtnW;       // 88
        int navX = margin + (coverSize - navTotalW) / 2;          // 相对封面列居中
        int navY = y + coverSize + 2;
        coverNavPanel = new Panel
        {
            Location = new Point(navX, navY),
            Size = new Size(navTotalW, navRowH),
            BackColor = BackColor,
            Visible = false
        };
        coverPrevBtn = new Button
        {
            Text = "◀",
            Location = new Point(0, 0),
            Size = new Size(navBtnW, navBtnH),
            Font = navFont,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = LabelColor,
            Enabled = false,
            Cursor = Cursors.Hand
        };
        coverPrevBtn.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        coverPrevBtn.Click += OnCoverPrevClick;

        coverIndexLabel = new Label
        {
            Text = "",
            Location = new Point(navBtnW + 2, 2),
            Width = indexW,
            Height = navBtnH,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = navFont,
            ForeColor = LabelColor
        };

        coverNextBtn = new Button
        {
            Text = "▶",
            Location = new Point(navBtnW + indexW + 4, 0),
            Size = new Size(navBtnW, navBtnH),
            Font = navFont,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = LabelColor,
            Enabled = false,
            Cursor = Cursors.Hand
        };
        coverNextBtn.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        coverNextBtn.Click += OnCoverNextClick;
        coverNavPanel.Controls.AddRange(new Control[] { coverPrevBtn, coverIndexLabel, coverNextBtn });

        overwriteCoverCheck = new CheckBox
        {
            Text = "覆盖",
            Location = new Point(infoX, y + coverSize - 22),
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = LabelColor,
            Checked = true
        };

        coverPanel.Controls.Add(coverPictureBox);
        y += coverSize + navRowH + 10;   // 封面 + 下方导航行 + 间距

        // ====== 封面右键菜单 ======
        var coverMenu = new ContextMenuStrip();

        var addCoverMI = new ToolStripMenuItem("添加封面");
        addCoverMI.DropDownItems.Add(new ToolStripMenuItem("选择本地文件...", null, (_, _) => CoverOpenRequested?.Invoke(this, EventArgs.Empty)));
        addCoverMI.DropDownItems.Add(new ToolStripMenuItem("从网络搜索..."));
        addCoverMI.DropDownItems.Add(new ToolStripMenuItem("从文件标签中选择..."));

        var compressCoverMI = new ToolStripMenuItem("更改分辨率...", null, (_, _) => CoverCompressRequested?.Invoke(this, EventArgs.Empty));
        var deleteCoverMI = new ToolStripMenuItem("删除封面", null, (_, _) => CoverDeleteRequested?.Invoke(this, EventArgs.Empty));
        var openCoverMI = new ToolStripMenuItem("打开封面", null, (_, _) => CoverOpenExternalRequested?.Invoke(this, EventArgs.Empty));
        var extractCoverMI = new ToolStripMenuItem("提取封面...", null, (_, _) => CoverExtractRequested?.Invoke(this, EventArgs.Empty));

        // 封面类型子菜单
        var coverTypeMI = new ToolStripMenuItem("封面类型");
        foreach (var pt in new (CoverPictureType Type, string Name)[]
        {
            (CoverPictureType.FrontCover, "封面"),
            (CoverPictureType.BackCover, "封底"),
            (CoverPictureType.LeafletPage, "插页"),
            (CoverPictureType.Media, "介质"),
            (CoverPictureType.LeadArtist, "主要艺术家"),
            (CoverPictureType.Artist, "艺术家"),
            (CoverPictureType.Conductor, "指挥"),
            (CoverPictureType.Band, "乐队"),
            (CoverPictureType.Composer, "作曲家"),
            (CoverPictureType.Lyricist, "作词家"),
            (CoverPictureType.RecordingLocation, "录制地点"),
            (CoverPictureType.DuringRecording, "录制中"),
            (CoverPictureType.DuringPerformance, "表演中"),
            (CoverPictureType.MovieScreenCapture, "电影截图"),
            (CoverPictureType.Illustration, "插图"),
            (CoverPictureType.BandLogo, "乐队标志"),
            (CoverPictureType.PublisherLogo, "出版商标志"),
            (CoverPictureType.Other, "其他"),
        })
        {
            var item = new ToolStripMenuItem(pt.Name);
            item.Click += (_, _) => CoverTypeChanged?.Invoke(this, pt.Type);
            coverTypeMI.DropDownItems.Add(item);
        }

        coverMenu.Items.AddRange(new ToolStripItem[] {
            addCoverMI,
            compressCoverMI,
            deleteCoverMI,
            openCoverMI,
            extractCoverMI,
            new ToolStripSeparator(),
            coverTypeMI,
        });
        coverPictureBox.ContextMenuStrip = coverMenu;

        // ====== 工具提示 ======
        _toolTip = new ToolTip();
        SetEncBtnTooltips();

        // 添加到面板
        Controls.AddRange(new Control[] {
            titleLabel, titleBox, titleEncBtn,
            artistLabel, artistBox, artistEncBtn,
            albumLabel, albumBox, albumEncBtn,
            yearLabel, yearBox, yearEncBtn,
            trackLabel, discLabel, trackNumeric, discNumeric,
            genreLabel, genreBox, genreEncBtn,
            albumArtistLabel, albumArtistBox, albumArtistEncBtn,
            composerLabel, composerBox, composerEncBtn,
            lyricistLabel, lyricistBox, lyricistEncBtn,
            commentLabel, commentBox, commentEncBtn,
            lyricLabel, lyricPreviewBox, lyricEditBtn, lyricEncBtn,
            separator,
            coverSectionLabel, coverPanel,
            coverFormatLabel, coverResolutionLabel, coverSizeLabel, coverTypeLabel,
            coverNavPanel,
            overwriteCoverCheck
        });

        ResumeLayout(false);
        PerformLayout();
    }

    // ====== 公开属性 ======

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Image? CoverImage
    {
        get => coverPictureBox.Image;
        set => coverPictureBox.Image = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool OverwriteCover
    {
        get => overwriteCoverCheck.Checked;
        set => overwriteCoverCheck.Checked = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CurrentPictureIndex => _currentPictureIndex;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public List<CoverArt>? Pictures => _pictures;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string LyricsPreview
    {
        get => lyricPreviewBox.Text;
        set => lyricPreviewBox.Text = value ?? "";
    }

    public void SetLyricPreview(string? lyrics)
    {
        string? firstLine = null;
        if (!string.IsNullOrEmpty(lyrics))
        {
            var norm = lyrics.Replace("\r\n", "\n").Replace("\r", "\n");
            firstLine = norm.Split('\n')[0].Trim('\n', '\r');
            if (firstLine.Length > 40)
                firstLine = firstLine.Substring(0, 40) + "…";
        }

        PopulateCombo(lyricPreviewBox, firstLine);
        lyricPreviewBox.Text = firstLine ?? "";
        lyricPreviewBox.ForeColor = LabelColor;
    }

    // ====== 数据加载/保存 ======

    /// <summary>初始化下拉文本框的选项（保留原值 + &lt;keep&gt; + &lt;blank&gt;）</summary>
    private static void PopulateCombo(ComboBox cb, string? currentValue)
    {
        cb.Items.Clear();
        cb.Items.Add("<keep>");
        cb.Items.Add("<blank>");
        if (!string.IsNullOrEmpty(currentValue))
            cb.Items.Add(currentValue);
    }

    /// <summary>仅刷新左侧栏字段文本框内容，保留已有的"(已修改)"标记</summary>
    public void UpdateFieldTexts(MusicFile file)
    {
        _currentFile = file;
        titleBox.Text = file.Title;
        artistBox.Text = file.Artist;
        albumBox.Text = file.Album;
        yearBox.Text = file.Year > 0 ? file.Year.ToString() : "";
        trackNumeric.Text = file.Track > 0 ? file.Track.ToString() : "";
        discNumeric.Text = file.Disc > 0 ? file.Disc.ToString() : "";
        genreBox.Text = file.Genre;
        albumArtistBox.Text = file.AlbumArtist;
        composerBox.Text = file.Composer;
        lyricistBox.Text = file.Lyricist;
        commentBox.Text = file.Comment;
        if (string.IsNullOrEmpty(file.Lyrics))
            lyricPreviewBox.Text = "";
        else
        {
            var norm = file.Lyrics.Replace("\r\n", "\n");
            var firstLine = norm.Split('\n')[0].Trim('\n', '\r');
            lyricPreviewBox.Text = firstLine.Length > 40 ? firstLine.Substring(0, 40) + "…" : firstLine;
            lyricPreviewBox.ForeColor = LabelColor;
        }
        UpdateCoverInfo(file);
    }

    public void LoadFromFile(MusicFile file)
    {
        _currentFile = file;
        ResetFieldLabels();

        _loading = true;
        titleBox.Text = file.Title;
        PopulateCombo(titleBox, file.Title);
        artistBox.Text = file.Artist;
        PopulateCombo(artistBox, file.Artist);
        albumBox.Text = file.Album;
        PopulateCombo(albumBox, file.Album);
        yearBox.Text = file.Year > 0 ? file.Year.ToString() : "";
        PopulateCombo(yearBox, file.Year > 0 ? file.Year.ToString() : null);
        trackNumeric.Text = file.Track > 0 ? file.Track.ToString() : "";
        PopulateCombo(trackNumeric, file.Track > 0 ? file.Track.ToString() : null);
        discNumeric.Text = file.Disc > 0 ? file.Disc.ToString() : "";
        PopulateCombo(discNumeric, file.Disc > 0 ? file.Disc.ToString() : null);
        genreBox.Text = file.Genre;
        PopulateCombo(genreBox, file.Genre);
        albumArtistBox.Text = file.AlbumArtist;
        PopulateCombo(albumArtistBox, file.AlbumArtist);
        composerBox.Text = file.Composer;
        PopulateCombo(composerBox, file.Composer);
        lyricistBox.Text = file.Lyricist;
        PopulateCombo(lyricistBox, file.Lyricist);
        commentBox.Text = file.Comment;
        PopulateCombo(commentBox, file.Comment);
        SetLyricPreview(file.Lyrics);
        file.IsModified = false;
        _loading = false;

        // 封面信息
        UpdateCoverInfo(file);
    }

    /// <summary>多选模式：所有字段默认 &lt;keep&gt;，封面/歌词清空</summary>
    public void SetKeepMode()
    {
        _currentFile = null;
        ResetFieldLabels();
        _loading = true;

        PopulateCombo(titleBox, null);
        titleBox.Text = "<keep>";
        PopulateCombo(artistBox, null);
        artistBox.Text = "<keep>";
        PopulateCombo(albumBox, null);
        albumBox.Text = "<keep>";
        yearBox.Text = "<keep>";
        trackNumeric.Text = "<keep>";
        discNumeric.Text = "<keep>";
        PopulateCombo(genreBox, null);
        genreBox.Text = "<keep>";
        PopulateCombo(albumArtistBox, null);
        albumArtistBox.Text = "<keep>";
        PopulateCombo(composerBox, null);
        composerBox.Text = "<keep>";
        PopulateCombo(lyricistBox, null);
        lyricistBox.Text = "<keep>";
        PopulateCombo(commentBox, null);
        commentBox.Text = "<keep>";
        PopulateCombo(lyricPreviewBox, null);
        lyricPreviewBox.Text = "<keep>";

        _loading = false;

        UpdateCoverInfo(null);
    }

    /// <summary>更新封面信息标签</summary>
    public void UpdateCoverInfo(MusicFile? file)
    {
        if (file == null || !file.HasCoverArt || file.CoverArtData == null || file.CoverArtData.Length == 0)
        {
            coverFormatLabel.Text = "--";
            coverResolutionLabel.Text = "--";
            coverSizeLabel.Text = "--";
            coverTypeLabel.Text = "--";
            UpdateNavState();
            return;
        }

        try
        {
            using var ms = new System.IO.MemoryStream(file.CoverArtData);
            // validateImageData=false 只读头部，不解析全部像素数据，速度快很多
            using var img = Image.FromStream(ms, false, false);
            var fmt = img.RawFormat;
            string fmtName = fmt.Equals(System.Drawing.Imaging.ImageFormat.Jpeg) ? "JPEG" :
                             fmt.Equals(System.Drawing.Imaging.ImageFormat.Png) ? "PNG" :
                             fmt.Equals(System.Drawing.Imaging.ImageFormat.Bmp) ? "BMP" :
                             fmt.Equals(System.Drawing.Imaging.ImageFormat.Gif) ? "GIF" : fmt.ToString();
            coverFormatLabel.Text = fmtName;
            coverResolutionLabel.Text = $"{img.Width} x {img.Height}";
            coverSizeLabel.Text = FormatFileSize(file.CoverArtData.Length);
            coverTypeLabel.Text = file.CoverArtType ?? "其他";
        }
        catch
        {
            coverFormatLabel.Text = "--";
            coverResolutionLabel.Text = "--";
            coverSizeLabel.Text = FormatFileSize(file.CoverArtData.Length);
            coverTypeLabel.Text = "--";
        }
        UpdateNavState();
    }

    /// <summary>加载多张图片列表供导航</summary>
    public void LoadPictures(List<CoverArt>? pictures, int currentIndex)
    {
        _pictures = pictures;
        _currentPictureIndex = currentIndex;
        UpdateNavState();
    }

    /// <summary>更新导航按钮和索引标签状态</summary>
    private void UpdateNavState()
    {
        bool hasNav = _pictures != null && _pictures.Count > 1;
        coverNavPanel.Visible = hasNav;

        if (hasNav)
        {
            if (_currentPictureIndex < 0) _currentPictureIndex = 0;
            if (_currentPictureIndex >= _pictures!.Count) _currentPictureIndex = _pictures.Count - 1;
            coverIndexLabel.Text = $"{_currentPictureIndex + 1}/{_pictures.Count}";
            coverPrevBtn.Enabled = _currentPictureIndex > 0;
            coverNextBtn.Enabled = _currentPictureIndex < _pictures.Count - 1;
        }
        else
        {
            coverIndexLabel.Text = "";
            coverPrevBtn.Enabled = false;
            coverNextBtn.Enabled = false;
        }
    }

    private void OnCoverPrevClick(object? sender, EventArgs e)
    {
        if (_pictures == null || _currentPictureIndex <= 0) return;
        _currentPictureIndex--;
        CoverIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCoverNextClick(object? sender, EventArgs e)
    {
        if (_pictures == null || _currentPictureIndex >= _pictures.Count - 1) return;
        _currentPictureIndex++;
        CoverIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1_024) return $"{bytes / 1_024.0:F0} KB";
        return $"{bytes} B";
    }

    /// <summary>返回 true 表示"跳过此字段（不修改）"</summary>
    private static bool IsKeep(string text) => text == "<keep>";
    /// <summary>返回 true 表示"清空此字段"</summary>
    private static bool IsBlank(string text) => text == "<blank>";

    /// <summary>处理保存：&lt;keep&gt; 不修改，&lt;blank&gt; 清空，否则写入文本</summary>
    private static string? SaveFieldText(ComboBox box) => SaveFieldText(box.Text);
    private static string? SaveFieldText(string text)
    {
        if (IsKeep(text)) return null;     // null = 不修改
        if (IsBlank(text)) return "";       // 空字符串 = 清空
        return text;                        // 实际值
    }

    public void SaveToFile(MusicFile file)
    {
        var t = SaveFieldText(titleBox);
        if (t != null) file.Title = t;
        t = SaveFieldText(artistBox);
        if (t != null) file.Artist = t;
        t = SaveFieldText(albumBox);
        if (t != null) file.Album = t;

        // 年份：<keep> 跳过，<blank> 或空 清空(=0)，有效数字更新，无效输入跳过
        // 注意：清空必须置 0（而非 null），因为 ApplyTagData 对 null 数字不写入，置 0 才能把旧值写掉
        if (IsKeep(yearBox.Text)) { /* 保留原值 */ }
        else if (IsBlank(yearBox.Text) || string.IsNullOrWhiteSpace(yearBox.Text)) file.Year = 0;
        else if (uint.TryParse(yearBox.Text, out var y)) file.Year = y;

        if (IsKeep(trackNumeric.Text)) { /* 保留原值 */ }
        else if (IsBlank(trackNumeric.Text) || string.IsNullOrWhiteSpace(trackNumeric.Text)) file.Track = 0;
        else if (uint.TryParse(trackNumeric.Text, out var trk)) file.Track = trk;

        if (IsKeep(discNumeric.Text)) { /* 保留原值 */ }
        else if (IsBlank(discNumeric.Text) || string.IsNullOrWhiteSpace(discNumeric.Text)) file.Disc = 0;
        else if (uint.TryParse(discNumeric.Text, out var d)) file.Disc = d;

        // 歌词：<keep> 不修改，<blank> 清空（其他情况已通过 UpdateLyrics 同步到 file.Lyrics）
        if (IsBlank(lyricPreviewBox.Text))
        {
            file.Lyrics = null;
            file.HasLyrics = false;
        }

        t = SaveFieldText(genreBox);
        if (t != null) file.Genre = t;
        t = SaveFieldText(albumArtistBox);
        if (t != null) file.AlbumArtist = t;
        t = SaveFieldText(composerBox);
        if (t != null) file.Composer = t;
        t = SaveFieldText(lyricistBox);
        if (t != null) file.Lyricist = t;
        t = SaveFieldText(commentBox);
        if (t != null) file.Comment = t;

        file.IsModified = false;
    }

    public void UpdateLyrics(string lyrics)
    {
        if (_currentFile != null)
        {
            _currentFile.Lyrics = lyrics;
            _currentFile.HasLyrics = !string.IsNullOrEmpty(lyrics);
            _currentFile.IsModified = true;
            SetLyricPreview(lyrics);
            MarkFieldModified("lyrics");
        }
    }

    /// <summary>仅刷新歌词预览，不标记已修改（用于已保存后的同步）</summary>
    public void UpdateLyricsPreview(string lyrics)
    {
        if (_currentFile != null)
        {
            _currentFile.Lyrics = lyrics;
            _currentFile.HasLyrics = !string.IsNullOrEmpty(lyrics);
            SetLyricPreview(lyrics);
        }
    }

    // ====== 编码修正 ======

    /// <summary>获取指定字段的文本值</summary>
    public string GetFieldText(string fieldName)
    {
        return fieldName.ToLowerInvariant() switch
        {
            "title" => titleBox.Text,
            "artist" => artistBox.Text,
            "album" => albumBox.Text,
            "year" => yearBox.Text,
            "genre" => genreBox.Text,
            "albumartist" => albumArtistBox.Text,
            "composer" => composerBox.Text,
            "lyricist" => lyricistBox.Text,
            "comment" => commentBox.Text,
            "lyrics" => _currentFile?.Lyrics ?? lyricPreviewBox.Text,
            "track" => trackNumeric.Text,
            "disc" => discNumeric.Text,
            _ => ""
        };
    }

    /// <summary>设置指定字段的文本值</summary>
    public void SetFieldText(string fieldName, string value)
    {
        switch (fieldName.ToLowerInvariant())
        {
            case "title": titleBox.Text = value; break;
            case "artist": artistBox.Text = value; break;
            case "album": albumBox.Text = value; break;
            case "year": yearBox.Text = value; break;
            case "genre": genreBox.Text = value; break;
            case "albumartist": albumArtistBox.Text = value; break;
            case "composer": composerBox.Text = value; break;
            case "lyricist": lyricistBox.Text = value; break;
            case "comment": commentBox.Text = value; break;
            case "track":
            case "trackstr":
                trackNumeric.Text = value; break;
            case "disc":
            case "discstr":
                discNumeric.Text = value; break;
            case "lyrics":
                if (_currentFile != null)
                {
                    _currentFile.Lyrics = value;
                    _currentFile.HasLyrics = !string.IsNullOrEmpty(value);
                    _currentFile.IsModified = true;
                    SetLyricPreview(value);
                }
                break;
        }
    }

    // ====== 字段修改标记（(已修改) 标签） ======

    /// <summary>注册字段名与标签控件的对应关系</summary>
    private void RegisterFieldLabel(string fieldName, Label label)
    {
        if (!string.IsNullOrEmpty(fieldName) && label != null)
            _fieldLabels[fieldName] = (label, label.Text);
    }

    /// <summary>重置所有字段标签为原始名称，清除"(已修改)"标记</summary>
    public void ResetFieldLabels()
    {
        var keys = new List<string>(_fieldLabels.Keys);
        foreach (var key in keys)
        {
            var entry = _fieldLabels[key];
            entry.Label.Text = entry.OriginalText;
            _fieldLabels[key] = entry; // 保留 OriginalText 不变
        }
    }

    /// <summary>将指定字段的标签标记为"(已修改)"</summary>
    private void MarkFieldModified(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName) || !_fieldLabels.TryGetValue(fieldName, out var entry))
            return;
        if (!entry.Label.Text.EndsWith("(已修改)"))
            entry.Label.Text = entry.OriginalText + "(已修改)";
    }

    // ====== 辅助方法 ======

    private void SetEncBtnTooltips()
    {
        _toolTip.SetToolTip(titleEncBtn, "编码修正");
        _toolTip.SetToolTip(artistEncBtn, "编码修正");
        _toolTip.SetToolTip(albumEncBtn, "编码修正");
        _toolTip.SetToolTip(yearEncBtn, "编码修正");
        _toolTip.SetToolTip(genreEncBtn, "编码修正");
        _toolTip.SetToolTip(albumArtistEncBtn, "编码修正");
        _toolTip.SetToolTip(composerEncBtn, "编码修正");
        _toolTip.SetToolTip(lyricistEncBtn, "编码修正");
        _toolTip.SetToolTip(commentEncBtn, "编码修正");
        _toolTip.SetToolTip(lyricEncBtn, "编码修正");
    }

    private void OnFieldChanged(object? sender, EventArgs e)
    {
        if (_currentFile == null || _loading) return;
        _currentFile.IsModified = true;

        // 标记对应的字段标签为"(已修改)"
        if (sender is Control c && c.Tag is string fieldName && !string.IsNullOrEmpty(fieldName))
            MarkFieldModified(fieldName);
    }

    private static Label CreateLabel(string text, int x, int y, int width) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Width = width,
        Height = 23,
        TextAlign = ContentAlignment.MiddleRight,
        Font = new Font("Microsoft YaHei UI", 9F),
        ForeColor = LabelColor,
        Padding = new Padding(0, 0, 4, 0)
    };

    private static TextBox CreateTextBox(int x, int y, int width) => new()
    {
        Location = new Point(x, y),
        Width = width,
        Height = 23,
        Font = new Font("Microsoft YaHei UI", 9.5F),
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = InputBg
    };

    private Button CreateEncBtn(int x, int y, string fieldName)
    {
        var btn = new Button
        {
            Size = new Size(IconBtnWidth, 23),
            Location = new Point(x, y),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleCenter,
            ImageAlign = ContentAlignment.MiddleCenter,
            Tag = fieldName
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        btn.FlatAppearance.BorderSize = 1;
        btn.Image = IconHelper.GetCharsetIcon();
        btn.Click += (s, e) =>
        {
            string text = GetFieldText(fieldName);
            EncodingFixRequested?.Invoke(this, new EncodingFixEventArgs(fieldName, text));
        };
        return btn;
    }
}

/// <summary>编码修正事件参数</summary>
public class EncodingFixEventArgs : EventArgs
{
    public string FieldName { get; }
    public string Text { get; }

    public EncodingFixEventArgs(string fieldName, string text)
    {
        FieldName = fieldName;
        Text = text;
    }
}
