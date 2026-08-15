using System.Runtime.InteropServices;
using FontAwesome.Sharp;
using MusicTagClone.Interfaces;
using MusicTagClone.Models;
using MusicTagClone.Services;

namespace MusicTagClone.Forms;

/// <summary>
/// 歌词编辑窗口 — 独立的歌词编辑对话框，支持LRC编辑、在线搜索、保存、查找替换
/// </summary>
public partial class LyricsEditForm : Form
{
    private readonly MusicFile _file;
    private readonly ILyricService _lyricService;
    private readonly ITagService _tagService;
    private readonly ISettingsService _settings;

    private TextBox lyricTextBox;
    private Button searchBtn;
    private Button saveAsLrcBtn;
    private Button findReplaceBtn;
    private Button okBtn;
    private Button okAndSaveBtn;
    private Button cancelBtn;

    private bool _lyricsModified;

    // 多级撤销/重做
    private readonly List<string> _undoHistory = new();
    private int _undoPosition = -1;
    private bool _suppressUndoRecord;
    private readonly System.Windows.Forms.Timer _undoTimer = new() { Interval = 300 };

    /// <summary>用户点击的是"确定并保存"（true）还是"确定"（false）</summary>
    public bool Saved { get; private set; }

    public string Lyrics => lyricTextBox.Text;

    public LyricsEditForm(MusicFile file, ILyricService lyricService,
        ITagService tagService, ISettingsService settings)
    {
        _file = file;
        _lyricService = lyricService;
        _tagService = tagService;
        _settings = settings;

        InitializeComponent();
        LoadLyrics();
    }

    private void InitializeComponent()
    {
        Text = "歌词";
        Size = new Size(640, 480);
        MinimumSize = new Size(480, 300);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(245, 246, 248);

        // === 歌词编辑区 ===
        lyricTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 10.5F),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            AcceptsReturn = true,
            AcceptsTab = true,
            WordWrap = false
        };
        lyricTextBox.TextChanged += (s, e) =>
        {
            _lyricsModified = true;
            if (!_suppressUndoRecord)
            {
                _undoTimer.Stop();
                _undoTimer.Start();
            }
        };

        // === 底部按钮栏 ===
        const int btnHeight = 28;
        const int gap = 6;
        int btnY = (46 - btnHeight) / 2; // 垂直居中

        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            BackColor = Color.FromArgb(250, 251, 252)
        };

        // ---- 左侧按钮：搜索 / 另存为 / 查找替换 ----
        var iconColor = Color.FromArgb(96, 98, 102);

        searchBtn = new Button
        {
            Text = "搜索",
            Image = IconHelper.RenderIcon(IconChar.Search, iconColor),
            TextImageRelation = TextImageRelation.ImageBeforeText,
            FlatStyle = FlatStyle.Standard,
            Font = new Font("Microsoft YaHei UI", 9F),
            Size = new Size(80, btnHeight),
            Location = new Point(gap, btnY)
        };
        searchBtn.Click += (s, e) => ShowSearchMenu(searchBtn);

        saveAsLrcBtn = new Button
        {
            Text = "另存为LRC",
            Image = IconHelper.RenderIcon(IconChar.Save, iconColor),
            TextImageRelation = TextImageRelation.ImageBeforeText,
            FlatStyle = FlatStyle.Standard,
            Font = new Font("Microsoft YaHei UI", 9F),
            Size = new Size(104, btnHeight),
            Location = new Point(searchBtn.Right + gap, btnY)
        };
        saveAsLrcBtn.Click += async (s, e) => await SaveLrcAsync();

        findReplaceBtn = new Button
        {
            Text = "查找/替换",
            FlatStyle = FlatStyle.Standard,
            Font = new Font("Microsoft YaHei UI", 9F),
            Size = new Size(86, btnHeight),
            Location = new Point(saveAsLrcBtn.Right + gap, btnY)
        };
        findReplaceBtn.Click += (s, e) => ShowFindReplaceMenu(findReplaceBtn);

        // ---- 右侧按钮：确定 / 确定并保存 / 取消 ----
        cancelBtn = new Button
        {
            Text = "取消",
            FlatStyle = FlatStyle.Standard,
            Font = new Font("Microsoft YaHei UI", 9F),
            Size = new Size(66, btnHeight),
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        okAndSaveBtn = new Button
        {
            Text = "确定并保存",
            FlatStyle = FlatStyle.Standard,
            Font = new Font("Microsoft YaHei UI", 9F),
            Size = new Size(92, btnHeight),
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        okAndSaveBtn.Click += OnOkAndSaveClick;

        okBtn = new Button
        {
            Text = "确定",
            FlatStyle = FlatStyle.Standard,
            Font = new Font("Microsoft YaHei UI", 9F),
            Size = new Size(66, btnHeight),
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        okBtn.Click += OnOkClick;

        // 右侧按钮从右往左排，bottomPanel.Resize 时重算
        void LayoutRightButtons()
        {
            cancelBtn.Location = new Point(bottomPanel.Width - gap - 66, btnY);
            okAndSaveBtn.Location = new Point(cancelBtn.Left - gap - 92, btnY);
            okBtn.Location = new Point(okAndSaveBtn.Left - gap - 66, btnY);
        }
        LayoutRightButtons();
        bottomPanel.Resize += (s, e) => LayoutRightButtons();

        bottomPanel.Controls.AddRange(new Control[] {
            searchBtn, saveAsLrcBtn, findReplaceBtn,
            okBtn, okAndSaveBtn, cancelBtn
        });

        Controls.Add(lyricTextBox);
        Controls.Add(bottomPanel);

        // 键盘快捷键
        _undoTimer.Tick += (s, e) =>
        {
            _undoTimer.Stop();
            RecordUndoState();
        };

        KeyPreview = true;
        KeyDown += (s, e) =>
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                e.SuppressKeyPress = true;
                OnOkAndSaveClick(s, e);
            }
            else if (e.Control && e.KeyCode == Keys.F)
            {
                e.SuppressKeyPress = true;
                ShowFindReplaceMenu(findReplaceBtn);
            }
            else if (e.Control && e.Shift && e.KeyCode == Keys.Z)
            {
                if (_undoPosition < _undoHistory.Count - 1)
                {
                    _undoPosition++;
                    ApplyUndoState();
                    e.SuppressKeyPress = true;
                }
            }
            else if (e.Control && e.KeyCode == Keys.Z)
            {
                _undoTimer.Stop();
                if (_undoPosition == _undoHistory.Count - 1 &&
                    lyricTextBox.Text != _undoHistory[_undoPosition])
                    RecordUndoState();
                if (_undoPosition > 0)
                {
                    _undoPosition--;
                    ApplyUndoState();
                    e.SuppressKeyPress = true;
                }
            }
        };
    }

    private void LoadLyrics()
    {
        // WinForms TextBox(Multiline) 需要 \r\n 才换行, 但 TagLibSharp 对不同格式返回不同换行符
        // FLAC→\r\n, MP3→\n, M4A→\n。统一转成 \r\n
        var text = _file.Lyrics ?? "";
        text = text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        lyricTextBox.Text = text;
        _lyricsModified = false;
        _undoHistory.Clear();
        _undoHistory.Add(text);
        _undoPosition = 0;
    }

    private void RecordUndoState()
    {
        if (_undoPosition < _undoHistory.Count - 1)
            _undoHistory.RemoveRange(_undoPosition + 1, _undoHistory.Count - _undoPosition - 1);
        _undoHistory.Add(lyricTextBox.Text);
        if (_undoHistory.Count > 200)
        {
            _undoHistory.RemoveAt(0);
            _undoPosition--;
        }
        _undoPosition = _undoHistory.Count - 1;
    }

    private void ApplyUndoState()
    {
        _suppressUndoRecord = true;
        SendMessage(lyricTextBox.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        lyricTextBox.Text = _undoHistory[_undoPosition];
        SendMessage(lyricTextBox.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
        lyricTextBox.Invalidate();
        _suppressUndoRecord = false;
        _lyricsModified = _undoPosition > 0;
        lyricTextBox.Select(lyricTextBox.Text.Length, 0);
        lyricTextBox.ScrollToCaret();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    private const int WM_SETREDRAW = 0x000B;

    // === 搜索歌词 ===
    private async void ShowSearchMenu(Button btn)
    {
        var menu = new ContextMenuStrip();
        var sources = new[] { "网易云音乐", "QQ音乐", "酷狗音乐", "酷我音乐", "MiniLyrics" };

        foreach (var source in sources)
        {
            var item = new ToolStripMenuItem(source);
            var src = source;
            item.Click += async (s, e) => await SearchLyricFromSource(src);
            menu.Items.Add(item);
        }

        menu.Show(btn, new Point(0, btn.Height));
    }

    private async Task SearchLyricFromSource(string source)
    {
        try
        {
            Cursor = Cursors.WaitCursor;

            var config = new LyricInfo.DownloadConfig
            {
                DownloadTranslation = _settings.LyricDownload_DownloadTrans_Enable,
                DontDownloadOriginal = _settings.LyricDownload_DownloadTrans_DontDownloadOrigLyric,
                ReformatTimetag = _settings.LyricDownload_ReformatTimetag,
                RemoveTimetag = _settings.LyricDownload_RemoveTimetag,
                DeleteHeadTag = _settings.LyricDownload_DeleteHeadTag,
                DeleteBlankLines = _settings.LyricDownload_DeleteLinesOfBlankText,
                ChineseConvMode = _settings.LyricDownload_DownloadTrans_ChineseConvMode ?? "none"
            };

            var condition = SearchCondition.FromSettings(_settings);
            condition.WebSearchItemsLimit = _settings.WebSearchItemsLimit;

            var results = await _lyricService.SearchLyricsAsync(_file, condition, config);

            if (results.Count == 0)
            {
                MessageBox.Show(this, "未找到匹配的歌词", "搜索结果",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 显示搜索结果供选择
            using var selectDlg = new Form
            {
                Text = $"选择歌词 — {source}",
                Size = new Size(550, 400),
                StartPosition = FormStartPosition.CenterParent
            };
            var listBox = new ListBox { Dock = DockStyle.Fill, DisplayMember = "ToString" };
            listBox.Items.AddRange(results.Cast<object>().ToArray());
            var selectBtn = new Button { Text = "下载", Dock = DockStyle.Bottom, Height = 35 };
            selectBtn.Click += async (s2, e2) =>
            {
                if (listBox.SelectedItem is SearchResult sr)
                {
                    var lyric = await _lyricService.DownloadLyricAsync(sr, config);
                    if (lyric != null)
                    {
                        lyricTextBox.Text = lyric.LrcFormatted ?? lyric.OriginalLyric ?? "";
                        _lyricsModified = true;
                        selectDlg.Close();
                    }
                }
            };
            selectDlg.Controls.Add(listBox);
            selectDlg.Controls.Add(selectBtn);
            selectDlg.ShowDialog(this);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    // === 另存为Lrc（默认 UTF-8 保存到文件） ===
    private async Task SaveLrcAsync()
    {
        if (string.IsNullOrEmpty(lyricTextBox.Text)) return;

        try
        {
            Cursor = Cursors.WaitCursor;

            var lyric = new LyricInfo
            {
                OriginalLyric = lyricTextBox.Text,
                LrcFormatted = _settings.LyricDownload_ReformatTimetag
                    ? _lyricService.ReformatTimetag(lyricTextBox.Text) : lyricTextBox.Text
            };

            var saved = await _lyricService.SaveLrcFileAsync(_file.Directory, _file, lyric,
                new LyricInfo.SaveConfig
                {
                    SaveDirectory = _settings.SaveLrcDirectory ?? _file.Directory,
                    FilenameFormat = _settings.SaveLrcFilenameFormat ?? "{artist} - {title}.lrc",
                    FileDefaultEncoding = "utf-8"
                });

            MessageBox.Show(this, saved != null ? "LRC 已保存" : "保存失败",
                "保存LRC", MessageBoxButtons.OK,
                saved != null ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    // === 查找替换 ===
    private Form? _findReplaceDlg;

    private void ShowFindReplaceMenu(Button btn)
    {
        // 已打开则激活
        if (_findReplaceDlg != null && !_findReplaceDlg.IsDisposed)
        {
            _findReplaceDlg.Activate();
            return;
        }

        int left = 12, lblW = 56, boxW = 250, btnColX = left + lblW + boxW + 6;
        int btnW = 46, btnH = 28, btnGap = 4, btnColW = btnW * 2 + btnGap;
        int row1Y = 10, row2Y = 40, row3Y = 70;

        var dlg = new Form
        {
            Text = "查找/替换",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.Manual,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowIcon = false,
            ShowInTaskbar = false,
        };
        _findReplaceDlg = dlg;
        dlg.FormClosed += (_, _) => _findReplaceDlg = null;

        // 定位在歌词框中间
        var lyricsRect = this.RectangleToScreen(lyricTextBox.Bounds);
        dlg.Location = new Point(
            lyricsRect.Left + (lyricsRect.Width - dlg.Width) / 2,
            lyricsRect.Top + (lyricsRect.Height - dlg.Height) / 2
        );

        var findLabel = new Label { Text = "查找(&N):", Location = new Point(left, row1Y), Width = lblW, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
        var findBox = new TextBox { Location = new Point(left + lblW, row1Y), Width = boxW, Font = new Font("Microsoft YaHei UI", 9F) };

        var replaceLabel = new Label { Text = "替换(&P):", Location = new Point(left, row2Y), Width = lblW, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
        var replaceBox = new TextBox { Location = new Point(left + lblW, row2Y), Width = boxW, Font = new Font("Microsoft YaHei UI", 9F) };

        // 右侧按钮列 — 箭头图标按钮（AngleUp/AngleDown）
        btnW = 32;
        btnColW = btnW * 2 + btnGap;
        var findPrevBtn = new Button
        {
            Text = "", Width = btnW, Height = btnH,
            Image = IconHelper.RenderIcon(IconChar.AngleUp, Color.Black, Color.White, iconSize: 18, bitmapSize: 20),
            FlatStyle = FlatStyle.Standard,
        };
        findPrevBtn.Location = new Point(btnColX, row1Y);

        var findNextBtn = new Button
        {
            Text = "", Width = btnW, Height = btnH,
            Image = IconHelper.RenderIcon(IconChar.AngleDown, Color.Black, Color.White, iconSize: 18, bitmapSize: 20),
            FlatStyle = FlatStyle.Standard,
        };
        findNextBtn.Location = new Point(findPrevBtn.Right + btnGap, row1Y);

        Button StdBtn(string text) => new()
        {
            Text = text, Width = btnColW, Height = btnH,
            FlatStyle = FlatStyle.Standard, Font = new Font("Microsoft YaHei UI", 9F),
        };
        var replaceBtn = StdBtn("替换"); replaceBtn.Location = new Point(btnColX, row2Y);
        var replaceAllBtn = StdBtn("全部替换"); replaceAllBtn.Location = new Point(btnColX, row3Y);
        var closeBtn = StdBtn("取消"); closeBtn.Location = new Point(btnColX, row3Y + btnH + 2);

        var matchCaseCheck = new CheckBox
        {
            Text = "区分大小写(&C)",
            Location = new Point(left, closeBtn.Bottom + 2),
            Width = 130,
            Height = 22,
            Font = new Font("Microsoft YaHei UI", 9F),
        };

        // 根据底部控件计算客户端区高度（不含标题栏）
        dlg.ClientSize = new Size(btnColX + btnColW + 12, matchCaseCheck.Bottom + 6);

        StringComparison GetComparison() => matchCaseCheck.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        System.Text.RegularExpressions.RegexOptions GetRegexOptions() => matchCaseCheck.Checked
            ? System.Text.RegularExpressions.RegexOptions.None
            : System.Text.RegularExpressions.RegexOptions.IgnoreCase;

        void FindNext(bool forward)
        {
            var searchText = findBox.Text;
            if (string.IsNullOrEmpty(searchText)) return;
            var comparison = GetComparison();

            if (forward)
            {
                var start = lyricTextBox.SelectionStart + lyricTextBox.SelectionLength;
                var idx = lyricTextBox.Text.IndexOf(searchText, start, comparison);
                if (idx < 0 && start > 0)
                    idx = lyricTextBox.Text.IndexOf(searchText, 0, comparison);
                if (idx >= 0)
                {
                    lyricTextBox.Select(idx, searchText.Length);
                    lyricTextBox.ScrollToCaret();
                    lyricTextBox.Focus();
                }
                else
                    MessageBox.Show(dlg, "未找到指定文本", "查找/替换", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                int start = lyricTextBox.SelectionStart;
                if (start <= 0)
                    start = lyricTextBox.Text.Length;
                var before = lyricTextBox.Text.Substring(0, start);
                var idx = before.LastIndexOf(searchText, comparison);
                if (idx < 0 && start < lyricTextBox.Text.Length)
                    idx = lyricTextBox.Text.LastIndexOf(searchText, comparison);
                if (idx >= 0)
                {
                    lyricTextBox.Select(idx, searchText.Length);
                    lyricTextBox.ScrollToCaret();
                    lyricTextBox.Focus();
                }
                else
                    MessageBox.Show(dlg, "未找到指定文本", "查找/替换", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        findPrevBtn.Click += (s2, e2) => FindNext(false);
        findNextBtn.Click += (s2, e2) => FindNext(true);

        replaceBtn.Click += (s2, e2) =>
        {
            if (string.IsNullOrEmpty(findBox.Text)) return;
            var comparison = GetComparison();
            if (lyricTextBox.SelectedText.Equals(findBox.Text, comparison))
            {
                lyricTextBox.SelectedText = replaceBox.Text;
                _lyricsModified = true;
            }
            FindNext(true);
        };

        replaceAllBtn.Click += (s2, e2) =>
        {
            if (!string.IsNullOrEmpty(findBox.Text))
            {
                var regexOptions = GetRegexOptions();
                var count = (lyricTextBox.Text.Length -
                    System.Text.RegularExpressions.Regex.Replace(
                        lyricTextBox.Text,
                        System.Text.RegularExpressions.Regex.Escape(findBox.Text),
                        "", regexOptions).Length) / findBox.Text.Length;

                lyricTextBox.Text = System.Text.RegularExpressions.Regex.Replace(
                    lyricTextBox.Text, System.Text.RegularExpressions.Regex.Escape(findBox.Text),
                    replaceBox.Text.Replace("$", "$$"), regexOptions);
                _lyricsModified = true;

                MessageBox.Show(dlg, $"已替换 {count} 处", "查找/替换",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };

        closeBtn.Click += (s2, e2) => dlg.Close();

        dlg.Controls.AddRange(new Control[] { findLabel, findBox, replaceLabel, replaceBox,
            findPrevBtn, findNextBtn, replaceBtn, replaceAllBtn, closeBtn, matchCaseCheck });

        // 非模式对话框：父窗口保持激活，歌词文本框可获取焦点
        dlg.Show(this);
    }

    // === 确定按钮 ===
    private void OnOkClick(object? sender, EventArgs e)
    {
        _file.Lyrics = lyricTextBox.Text.Replace("\r\n", "\n").Replace("\r", "\n");
        _file.HasLyrics = !string.IsNullOrEmpty(lyricTextBox.Text);
        _file.IsModified = true;
        Saved = false;
        DialogResult = DialogResult.OK;
        Close();
    }

    private async void OnOkAndSaveClick(object? sender, EventArgs e)
    {
        _file.Lyrics = lyricTextBox.Text.Replace("\r\n", "\n").Replace("\r", "\n");
        _file.HasLyrics = !string.IsNullOrEmpty(lyricTextBox.Text);

        try
        {
            Cursor = Cursors.WaitCursor;

            // 1. 写入音频文件标签
            await _tagService.WriteLyricsAsync(_file.FilePath, _file.Lyrics ?? "");

            // 2. 保存外部 LRC 文件
            var lyric = new LyricInfo
            {
                OriginalLyric = lyricTextBox.Text,
                LrcFormatted = _settings.LyricDownload_ReformatTimetag
                    ? _lyricService.ReformatTimetag(lyricTextBox.Text) : lyricTextBox.Text
            };

            await _lyricService.SaveLrcFileAsync(_file.Directory, _file, lyric,
                new LyricInfo.SaveConfig
                {
                    SaveDirectory = _settings.SaveLrcDirectory ?? _file.Directory,
                    FilenameFormat = _settings.SaveLrcFilenameFormat ?? "{artist} - {title}.lrc",
                    FileDefaultEncoding = _settings.SaveLrcFileDefaultEncoding ?? "utf-8"
                });

            _file.IsModified = false;
            Saved = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败: {ex.Message}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }
}
