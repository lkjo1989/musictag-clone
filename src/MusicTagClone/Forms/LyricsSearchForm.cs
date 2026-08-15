using MusicTagClone.Interfaces;
using MusicTagClone.Models;

namespace MusicTagClone.Forms;

/// <summary>
/// 歌词搜索对话框
/// 搜索指定源的歌词，以列表展示，选中后可预览歌词全文。
/// </summary>
internal class LyricsSearchForm : Form
{
    private readonly MusicFile _file;
    private readonly string _source;
    private readonly ILyricService _lyricService;
    private readonly ISettingsService _settings;

    // 控件
    private readonly TextBox _searchBox;
    private readonly Button _searchBtn;
    private readonly Label _sourceLabel;
    private readonly ProgressBar _loadingBar;
    private readonly Label _statusLabel;
    private readonly ListView _resultView;
    private readonly TextBox _lyricPreview;
    private readonly SplitContainer _splitContainer;
    private readonly Button _loadMoreBtn;
    private readonly Button _okBtn;
    private readonly Button _cancelBtn;

    // 状态
    private List<SearchResult> _results = new();
    private CancellationTokenSource? _searchCts;
    private int _searchLimit;
    private int _searchOffset;
    private bool _closing;

    /// <summary>用户选中的歌词（DialogResult == OK 时有值）</summary>
    public LyricInfo? SelectedLyric { get; private set; }

    /// <summary>用户选中的搜索结果</summary>
    public SearchResult? SelectedResult { get; private set; }

    public LyricsSearchForm(MusicFile file, string source,
        ILyricService lyricService, ISettingsService settings)
    {
        _file = file;
        _source = source;
        _lyricService = lyricService;
        _settings = settings;

        Text = $"搜索歌词 - {_file.Title} | {_file.Artist}";
        Size = new Size(750, 650);
        MinimumSize = new Size(600, 500);
        StartPosition = FormStartPosition.CenterParent;
        ShowIcon = false;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.White;

        var query = SearchCondition.FromSettings(_settings).BuildSearchQuery(_file);

        // ---- 搜索栏 ----
        _searchBox = new TextBox
        {
            Text = query,
            Location = new Point(12, 12),
            Width = 460,
            Font = new Font("Microsoft YaHei UI", 10F),
            BorderStyle = BorderStyle.FixedSingle,
        };

        _searchBtn = new Button
        {
            Text = "搜索",
            Location = new Point(478, 10),
            Width = 70,
            Height = 26,
            FlatStyle = FlatStyle.Standard,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.Black,
        };
        _searchBtn.Click += OnSearchClick;

        _sourceLabel = new Label
        {
            Text = GetSourceDisplayName(),
            Location = new Point(555, 12),
            Width = 175,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(96, 98, 102),
        };

        // ---- 进度条 ----
        _loadingBar = new ProgressBar
        {
            Location = new Point(12, 46),
            Width = 715,
            Height = 6,
            Style = ProgressBarStyle.Marquee,
            Visible = false,
        };

        // ---- 分割容器: 上半结果列表 + 下半歌词预览 ----
        _splitContainer = new SplitContainer
        {
            Location = new Point(12, 58),
            Width = 715,
            Height = 510,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 210,
            IsSplitterFixed = false,
            Panel1MinSize = 100,
            Panel2MinSize = 200,
        };

        // 上半: 结果列表
        _resultView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            Font = new Font("Microsoft YaHei UI", 9F),
            BackColor = Color.White,
        };
        _resultView.Columns.Add("标题", 180);
        _resultView.Columns.Add("艺术家", 140);
        _resultView.Columns.Add("专辑", 160);
        _resultView.Columns.Add("来源", 80);
        _resultView.SelectedIndexChanged += OnSelectionChanged;
        _resultView.DoubleClick += OnResultDoubleClick;
        _splitContainer.Panel1.Controls.Add(_resultView);

        // 下半: 歌词预览
        _lyricPreview = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9.5F),
            BackColor = Color.FromArgb(245, 246, 248),
            BorderStyle = BorderStyle.FixedSingle,
            Text = "选中结果后在此预览歌词...",
            ForeColor = Color.FromArgb(96, 98, 102),
        };
        _splitContainer.Panel2.Controls.Add(_lyricPreview);

        // ---- 状态文字 ----
        _statusLabel = new Label
        {
            Text = "",
            Location = new Point(12, 575),
            Width = 450,
            Height = 24,
            ForeColor = Color.FromArgb(96, 98, 102),
        };

        _loadMoreBtn = new Button
        {
            Text = "加载更多",
            Location = new Point(472, 573),
            Width = 96,
            Height = 30,
            Visible = _lyricService.SupportsPagination(_source),
            Enabled = false,
            FlatStyle = FlatStyle.Standard,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.Black,
        };
        _loadMoreBtn.Click += OnLoadMoreClick;

        // ---- 底部按钮 ----
        _okBtn = new Button
        {
            Text = "确定",
            Location = new Point(575, 573),
            Width = 70,
            Height = 30,
            Enabled = false,
            FlatStyle = FlatStyle.Standard,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.Black,
        };
        _okBtn.Click += OnOkClick;

        _cancelBtn = new Button
        {
            Text = "取消",
            Location = new Point(655, 573),
            Width = 70,
            Height = 30,
            FlatStyle = FlatStyle.Standard,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.Black,
        };
        _cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] {
            _searchBox, _searchBtn, _sourceLabel,
            _loadingBar, _splitContainer,
            _statusLabel, _loadMoreBtn, _okBtn, _cancelBtn
        });

        // 自动开始搜索
        Load += (_, _) => { if (!_closing) _ = PerformSearchAsync(false); };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        base.OnFormClosing(e);
    }

    // ============================================================
    // 搜索
    // ============================================================

    private async void OnSearchClick(object? sender, EventArgs e)
    {
        if (_closing || IsDisposed) return;
        await PerformSearchAsync(false);
    }

    private async void OnLoadMoreClick(object? sender, EventArgs e)
    {
        if (_closing || IsDisposed) return;
        await PerformSearchAsync(true);
    }

    private async Task PerformSearchAsync(bool loadMore)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        var query = _searchBox.Text.Trim();
        if (string.IsNullOrEmpty(query)) { _statusLabel.Text = "请输入搜索关键词"; return; }

        if (!loadMore)
        {
            _searchLimit = Math.Max(1, _settings.WebSearchItemsLimit);
            _searchOffset = 0;
            _results.Clear();
        }

        var requestOffset = loadMore ? _searchOffset + _searchLimit : 0;
        var requestLimit = _searchLimit;

        _loadingBar.Visible = true;
        _statusLabel.Text = "正在搜索歌词...";
        if (!loadMore) _resultView.Items.Clear();
        _lyricPreview.Text = "";
        _okBtn.Enabled = false;
        _loadMoreBtn.Enabled = false;

        try
        {
            var condition = new SearchCondition
            {
                CustomQuery = query,
                WebSearchItemsLimit = requestLimit,
                WebSearchItemsOffset = requestOffset,
            };
            var config = new LyricInfo.DownloadConfig
            {
                DownloadTranslation = _settings.LyricDownload_DownloadTrans_Enable,
                DontDownloadOriginal = _settings.LyricDownload_DownloadTrans_DontDownloadOrigLyric,
                ReformatTimetag = _settings.LyricDownload_ReformatTimetag,
                RemoveTimetag = _settings.LyricDownload_RemoveTimetag,
            };

            var results = await _lyricService.SearchLyricsFromSourceAsync(
                _file, _source, condition, config, ct);

            if (ct.IsCancellationRequested) return;

            if (_closing || IsDisposed) return;

            if (loadMore)
            {
                MergeResults(results);
                _searchOffset = requestOffset;
            }
            else
                _results = results.ToList();
            _loadingBar.Visible = false;

            if (_results.Count == 0)
            {
                _statusLabel.Text = "未找到匹配的歌词";
                return;
            }

            _statusLabel.Text = $"找到 {_results.Count} 个结果";
            _loadMoreBtn.Enabled = _loadMoreBtn.Visible && results.Count >= requestLimit;
            PopulateResults();
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            if (_closing || IsDisposed) return;
            _loadingBar.Visible = false;
            _loadMoreBtn.Enabled = _loadMoreBtn.Visible;
            _statusLabel.Text = $"搜索失败：{ex.Message}";
        }
    }

    private void MergeResults(IEnumerable<SearchResult> page)
    {
        var keys = new HashSet<string>(_results.Select(r => r.GetIdentityKey()), StringComparer.Ordinal);
        foreach (var result in page)
        {
            if (keys.Add(result.GetIdentityKey()))
                _results.Add(result);
        }
    }

    private void PopulateResults()
    {
        _resultView.BeginUpdate();
        _resultView.Items.Clear();

        foreach (var result in _results)
        {
            var item = new ListViewItem(result.Title ?? "(未知)")
            {
                Tag = result,
            };
            item.SubItems.Add(result.Artist ?? "");
            item.SubItems.Add(result.Album ?? "");
            item.SubItems.Add(result.SourceName ?? "");
            _resultView.Items.Add(item);
        }

        _resultView.EndUpdate();
    }

    // ============================================================
    // 选择 / 预览 / 确定
    // ============================================================

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        _okBtn.Enabled = _resultView.SelectedItems.Count > 0;
        if (!_closing && !IsDisposed)
            _ = LoadLyricPreviewAsync();
    }

    private void OnResultDoubleClick(object? sender, EventArgs e)
    {
        if (_resultView.SelectedItems.Count > 0 && !_closing && !IsDisposed)
            _ = ApplySelectedLyricAsync();
    }

    private async Task LoadLyricPreviewAsync()
    {
        if (_resultView.SelectedItems.Count == 0) return;
        var item = _resultView.SelectedItems[0];
        var result = item.Tag as SearchResult;
        if (result == null) return;

        _lyricPreview.Text = "正在加载歌词...";
        _lyricPreview.ForeColor = Color.FromArgb(96, 98, 102);

        try
        {
            var config = new LyricInfo.DownloadConfig
            {
                DownloadTranslation = _settings.LyricDownload_DownloadTrans_Enable,
                DontDownloadOriginal = _settings.LyricDownload_DownloadTrans_DontDownloadOrigLyric,
                ReformatTimetag = _settings.LyricDownload_ReformatTimetag,
                RemoveTimetag = _settings.LyricDownload_RemoveTimetag,
            };
            var lyric = await _lyricService.DownloadLyricAsync(result, config,
                _searchCts?.Token ?? CancellationToken.None);

            if (_closing || IsDisposed) return;

            if (lyric?.OriginalLyric != null)
            {
                // 使用合并后的歌词（如果有翻译），否则用原文
                var displayText = lyric.LrcFormatted ?? lyric.OriginalLyric;
                // WinForms TextBox(Multiline) 需要 \r\n 才换行, 但网络来源的歌词可能只有 \n
                var text = displayText
                    .Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
                _lyricPreview.Text = text;
                _lyricPreview.ForeColor = Color.Black;
            }
            else
            {
                _lyricPreview.Text = "该歌曲无可用歌词";
                _lyricPreview.ForeColor = Color.FromArgb(96, 98, 102);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            if (_closing || IsDisposed) return;
            _lyricPreview.Text = "歌词加载失败";
            _lyricPreview.ForeColor = Color.FromArgb(96, 98, 102);
        }
    }

    private async void OnOkClick(object? sender, EventArgs e) => await ApplySelectedLyricAsync();

    private async Task ApplySelectedLyricAsync()
    {
        if (_resultView.SelectedItems.Count == 0) return;
        var item = _resultView.SelectedItems[0];
        var result = item.Tag as SearchResult;
        if (result == null) return;

        _loadingBar.Visible = true;
        _statusLabel.Text = "正在下载歌词...";

        try
        {
            var config = new LyricInfo.DownloadConfig
            {
                DownloadTranslation = _settings.LyricDownload_DownloadTrans_Enable,
                DontDownloadOriginal = _settings.LyricDownload_DownloadTrans_DontDownloadOrigLyric,
                ReformatTimetag = _settings.LyricDownload_ReformatTimetag,
                RemoveTimetag = _settings.LyricDownload_RemoveTimetag,
            };
            var lyric = await _lyricService.DownloadLyricAsync(result, config,
                _searchCts?.Token ?? CancellationToken.None);

            if (_closing || IsDisposed) return;

            if (lyric == null)
            {
                _statusLabel.Text = "下载失败";
                return;
            }

            SelectedLyric = lyric;
            SelectedResult = result;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            if (_closing || IsDisposed) return;
            _statusLabel.Text = $"下载失败：{ex.Message}";
        }
        finally
        {
            if (!_closing && !IsDisposed)
                _loadingBar.Visible = false;
        }
    }

    // ============================================================
    // 辅助
    // ============================================================

    private string GetSourceDisplayName() => _source.ToLowerInvariant() switch
    {
        "netease" => "来源：网易云音乐",
        "qq" => "来源：QQ音乐",
        "kugou" => "来源：酷狗音乐",
        "kuwo" => "来源：酷我音乐",
        _ => $"来源：{_source}",
    };
}
