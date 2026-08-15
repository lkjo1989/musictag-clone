using MusicTagClone.Interfaces;
using MusicTagClone.Models;
using MusicTagClone.Services;

namespace MusicTagClone.Forms;

/// <summary>
/// 标签搜索对话框 — 从在线源搜索标签元数据
/// 用于「组合标签」功能，搜索结果包含标题、艺术家、专辑、年份、音轨号等。
/// </summary>
internal class TagSearchForm : Form
{
    private readonly MusicFile _file;
    private readonly string _source;
    private readonly ICoverService _coverService;
    private readonly ILyricService _lyricService;
    private readonly IImageCache _imageCache;
    private readonly ISettingsService _settings;

    // 控件
    private readonly TextBox _searchBox;
    private readonly Button _searchBtn;
    private readonly Label _sourceLabel;
    private readonly ProgressBar _loadingBar;
    private readonly Label _statusLabel;
    private readonly ListView _resultView;
    private readonly ImageList _thumbnailList;
    private readonly Button _loadMoreBtn;
    private readonly Button _okBtn;
    private readonly Button _cancelBtn;

    // 状态
    private List<SearchResult> _results = new();
    private CancellationTokenSource? _searchCts;
    private int _searchLimit;
    private int _searchOffset;

    /// <summary>用户选中的搜索结果（DialogResult == OK 时有值）</summary>
    public SearchResult? SelectedResult { get; private set; }

    public TagSearchForm(MusicFile file, string source, ICoverService coverService,
        ILyricService lyricService, IImageCache imageCache, ISettingsService settings)
    {
        _file = file;
        _source = source;
        _coverService = coverService;
        _lyricService = lyricService;
        _imageCache = imageCache;
        _settings = settings;

        Text = $"搜索标签 - {GetSourceDisplayName()} - {_file.Title} | {_file.Artist}";
        Size = new Size(760, 560);
        MinimumSize = new Size(560, 400);
        StartPosition = FormStartPosition.CenterParent;
        ShowIcon = false;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.White;

        // ---- 上方搜索栏 ----
        var query = CreateSearchCondition().BuildSearchQuery(_file);

        _searchBox = new TextBox
        {
            Text = query,
            Location = new Point(12, 12),
            Width = 460,
            Font = new Font("Microsoft YaHei UI", 10F),
            BorderStyle = BorderStyle.FixedSingle,
        };
        _searchBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = PerformSearchAsync(false); } };

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
            Width = 720,
            Height = 6,
            Style = ProgressBarStyle.Marquee,
            Visible = false,
        };

        // ---- 封面缩略图 ImageList ----
        _thumbnailList = new ImageList
        {
            ImageSize = new Size(64, 64),
            ColorDepth = ColorDepth.Depth32Bit,
        };

        // ---- 结果列表 (Details) ----
        _resultView = new ListView
        {
            Location = new Point(12, 56),
            Width = 720,
            Height = 400,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            Font = new Font("Microsoft YaHei UI", 9F),
            BackColor = Color.White,
            SmallImageList = _thumbnailList,
        };
        _resultView.Columns.AddRange(new[] {
            new ColumnHeader { Text = "", Width = 68 },
            new ColumnHeader { Text = "分辨率", Width = 70 },
            new ColumnHeader { Text = "歌词", Width = 40 },
            new ColumnHeader { Text = "标题", Width = 155 },
            new ColumnHeader { Text = "艺术家", Width = 110 },
            new ColumnHeader { Text = "专辑", Width = 145 },
            new ColumnHeader { Text = "年份", Width = 45 },
            new ColumnHeader { Text = "音轨", Width = 40 },
            new ColumnHeader { Text = "碟号", Width = 40 },
        });
        _resultView.SelectedIndexChanged += OnSelectionChanged;
        _resultView.DoubleClick += (_, _) => { if (_resultView.SelectedItems.Count > 0) ApplySelected(); };

        // ---- 状态文字 ----
        _statusLabel = new Label
        {
            Text = "",
            Location = new Point(12, 464),
            Width = 460,
            Height = 24,
            ForeColor = Color.FromArgb(96, 98, 102),
        };

        _loadMoreBtn = new Button
        {
            Text = "加载更多",
            Location = new Point(482, 462),
            Width = 96,
            Height = 30,
            Visible = _coverService.SupportsPagination(_source),
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
            Location = new Point(585, 462),
            Width = 70,
            Height = 30,
            Enabled = false,
            FlatStyle = FlatStyle.Standard,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.Black,
        };
        _okBtn.Click += (_, _) => ApplySelected();

        _cancelBtn = new Button
        {
            Text = "取消",
            Location = new Point(662, 462),
            Width = 70,
            Height = 30,
            FlatStyle = FlatStyle.Standard,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.Black,
        };
        _cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] {
            _searchBox, _searchBtn, _sourceLabel,
            _loadingBar, _resultView,
            _statusLabel, _loadMoreBtn, _okBtn, _cancelBtn
        });

        // 自动搜索
        Load += (_, _) => _ = PerformSearchAsync(false);
    }

    private SearchCondition CreateSearchCondition() => new()
    {
        UseTitle = _settings.SearchConditionUseTitle,
        UseArtist = _settings.SearchConditionUseArtist,
        UseAlbum = _settings.SearchConditionUseAlbum,
        ItunesCountry = _settings.ItunesSearchParamsCountry ?? "US",
        WebSearchItemsLimit = Math.Max(1, _settings.WebSearchItemsLimit),
    };

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        base.OnFormClosing(e);
    }

    // ============================================================
    // 搜索
    // ============================================================

    private async void OnSearchClick(object? sender, EventArgs e)
    {
        await PerformSearchAsync(false);
    }

    private async void OnLoadMoreClick(object? sender, EventArgs e)
    {
        await PerformSearchAsync(true);
    }

    private async Task PerformSearchAsync(bool loadMore)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        var query = _searchBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            _statusLabel.Text = "请输入搜索关键词";
            return;
        }

        if (!loadMore)
        {
            _searchLimit = Math.Max(1, _settings.WebSearchItemsLimit);
            _searchOffset = 0;
            _results.Clear();
        }

        var requestOffset = loadMore ? _searchOffset + _searchLimit : 0;
        var requestLimit = _searchLimit;

        _loadingBar.Visible = true;
        _statusLabel.Text = "正在搜索...";
        if (!loadMore) _resultView.Items.Clear();
        _thumbnailList.Images.Clear();
        _okBtn.Enabled = false;
        _loadMoreBtn.Enabled = false;

        try
        {
            var condition = new SearchCondition
            {
                UseArtist = true,
                UseAlbum = true,
                WebSearchItemsLimit = requestLimit,
                WebSearchItemsOffset = requestOffset,
                CustomQuery = query,
            };

            var results = (await _coverService.SearchTagsFromSourceAsync(
                _file, _source, condition, ct)).ToList();

            if (ct.IsCancellationRequested) return;

            if (loadMore)
            {
                MergeResults(results);
                _searchOffset = requestOffset;
            }
            else
                _results = results;
            _loadingBar.Visible = false;

            if (_results.Count == 0)
            {
                _statusLabel.Text = "未找到匹配的标签";
                return;
            }

            PopulateResults();
            _loadMoreBtn.Enabled = _loadMoreBtn.Visible && results.Count >= requestLimit;
            _statusLabel.Text = $"找到 {_results.Count} 个结果";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
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

        foreach (var r in _results)
        {
            var item = new ListViewItem("")
            {
                Tag = r,
                ImageIndex = -1,
            };
            item.SubItems.Add("");  // 分辨率
            item.SubItems.Add("");  // 歌词
            item.SubItems.Add(r.Title ?? "");
            item.SubItems.Add(r.Artist ?? "");
            item.SubItems.Add(r.Album ?? "");
            item.SubItems.Add(r.Year ?? "");
            item.SubItems.Add(r.ExtraFields.TryGetValue("track", out var t) ? t : "");
            item.SubItems.Add(r.ExtraFields.TryGetValue("disc", out var d) ? d : "");
            _resultView.Items.Add(item);
        }

        _resultView.EndUpdate();

        // 异步加载缩略图 + 歌词检查
        foreach (var item in _resultView.Items.Cast<ListViewItem>())
        {
            var result = item.Tag as SearchResult;
            if (result != null)
                _ = LoadThumbnailAsync(item, result);
        }
    }

    private async Task LoadThumbnailAsync(ListViewItem item, SearchResult result)
    {
        var ct = _searchCts?.Token ?? CancellationToken.None;
        try
        {
            // 并行加载缩略图和歌词检查
            var thumbTask = LoadCoverThumbAsync(result, ct);
            var lyricTask = CheckLyricAvailabilityAsync(result, ct);
            await Task.WhenAll(thumbTask, lyricTask);

            if (ct.IsCancellationRequested) return;

            var (thumb, origWidth, origHeight) = thumbTask.Result;
            var hasLyric = lyricTask.Result;

            BeginInvoke(() =>
            {
                if (_resultView.IsDisposed) { thumb?.Dispose(); return; }
                if (item.ListView == null || item.Tag != result) { thumb?.Dispose(); return; }

                // 设置缩略图
                if (thumb != null)
                {
                    var key = Guid.NewGuid().ToString();
                    _thumbnailList.Images.Add(key, thumb);
                    item.ImageKey = key;
                    // 显示原始图片分辨率（非缩略图分辨率）
                    if (origWidth > 0 && origHeight > 0)
                        item.SubItems[1].Text = $"{origWidth}×{origHeight}";
                }

                // 设置歌词列
                item.SubItems[2].Text = hasLyric ? "有" : "无";
            });
        }
        catch { /* 单个缩略图加载失败不影响其他 */ }
    }

    private async Task<(Bitmap? thumb, int origWidth, int origHeight)> LoadCoverThumbAsync(SearchResult result, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(result.CoverUrl)) return (null, 0, 0);

        // 酷我的 CoverUrl 是 pic API 端点，需要先解析获取实际图片 URL
        var imageUrl = result.CoverUrl;
        var src = result.SourceName?.ToLowerInvariant() ?? "";
        if (src.Contains("kuwo") && result.CoverUrl.Contains("artistpicserver.kuwo.cn"))
        {
            using var resolveHttp = _coverService.CreateHttpClientForSource(result.SourceName ?? "");
            resolveHttp.Timeout = TimeSpan.FromSeconds(10);
            imageUrl = await ResolveKuwoCoverUrlAsync(result.CoverUrl, resolveHttp, ct);
            if (string.IsNullOrEmpty(imageUrl)) return (null, 0, 0);
        }

        // 使用带缓存的下载（字节落入 cache\img\）
        var data = await _coverService.DownloadImageBytesAsync(imageUrl, result.SourceName ?? "", ct);
        if (data == null) return (null, 0, 0);

        // 保存完整图片到历史缓存目录（内容寻址去重，点确定时直接用）
        result.CoverTempPath = _imageCache.StoreHistory(data);

        // 读取原图尺寸并生成缩略图，两个搜索窗口共用同一解析逻辑。
        var (thumb, origW, origH) = IconHelper.CreateThumbnailWithResolution(data, 64);
        return (thumb, origW, origH);
    }

    /// <summary>酷我 pic API 返回实际图片 URL（如 http://...jpg），需要先解析再下载</summary>
    private static async Task<string?> ResolveKuwoCoverUrlAsync(string picApiUrl, HttpClient http, CancellationToken ct)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, picApiUrl);
            request.Headers.Add("Referer", "https://kuwo.cn");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.3; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/68.0.3440.106 Safari/537.36");
            var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            // 返回的是纯文本 URL，可能以 http 或 https 开头
            body = body.Trim();
            if (body.StartsWith("http://") || body.StartsWith("https://"))
            {
                // 升级为 HTTPS（kwcdn 域名不支持 HTTPS，但 kuwo.cn 域名支持）
                if (body.StartsWith("http://"))
                    body = body.Replace("http://img1.kwcdn.kuwo.cn/", "https://img1.kuwo.cn/")
                               .Replace("http://img2.kwcdn.kuwo.cn/", "https://img2.kuwo.cn/")
                               .Replace("http://img3.kwcdn.kuwo.cn/", "https://img3.kuwo.cn/")
                               .Replace("http://img4.kwcdn.kuwo.cn/", "https://img4.kuwo.cn/");
                return body;
            }
            return null;
        }
        catch { return null; }
    }

    private static bool SupportsLyricSearch(string? sourceName)
    {
        var src = sourceName?.ToLowerInvariant() ?? "";
        return src == "netease" || src == "qq" || src == "kuwo" || src == "kugou"
            || src == "网易云音乐" || src == "qq音乐" || src == "酷我音乐" || src == "酷狗音乐";
    }

    private async Task<bool> CheckLyricAvailabilityAsync(SearchResult result, CancellationToken ct)
    {
        if (!SupportsLyricSearch(result.SourceName)) return false;
        try
        {
            var condition = new SearchCondition { CustomQuery = $"{result.Artist} {result.Title}" };
            var config = new LyricInfo.DownloadConfig();
            var sourceKey = result.SourceName?.ToLowerInvariant() ?? "netease";
            // 中文源名映射为 API key
            if (sourceKey == "网易云音乐") sourceKey = "netease";
            else if (sourceKey == "qq音乐") sourceKey = "qq";
            else if (sourceKey == "酷我音乐") sourceKey = "kuwo";
            else if (sourceKey == "酷狗音乐") sourceKey = "kugou";
            var results = await _lyricService.SearchLyricsFromSourceAsync(
                _file, sourceKey, condition, config, ct);
            return results.Count > 0;
        }
        catch { return false; }
    }

    // ============================================================
    // 选择 / 确定
    // ============================================================

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        _okBtn.Enabled = _resultView.SelectedItems.Count > 0;
    }

    private void ApplySelected()
    {
        if (_resultView.SelectedItems.Count == 0) return;
        SelectedResult = _resultView.SelectedItems[0].Tag as SearchResult;
        DialogResult = DialogResult.OK;
        Close();
    }

    // ============================================================
    // 辅助
    // ============================================================

    private string GetSourceDisplayName() => _source.ToLowerInvariant() switch
    {
        "default" => "来源：所有",
        "netease" => "来源：网易云音乐",
        "qq" => "来源：QQ音乐",
        "itunes" => "来源：iTunes",
        "kuwo" => "来源：酷我音乐",
        "lastfm" => "来源：Last.fm",
        "musicbrainz" => "来源：MusicBrainz",
        "discogs" => "来源：Discogs",
        _ => $"来源：{_source}",
    };
}
