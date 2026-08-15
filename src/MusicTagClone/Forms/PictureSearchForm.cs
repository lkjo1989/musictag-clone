using MusicTagClone.Interfaces;
using MusicTagClone.Models;

namespace MusicTagClone.Forms;

/// <summary>
/// 封面搜索对话框
/// 搜索指定源的封面图片，以缩略图列表展示，用户选择后应用到当前文件。
/// 缓存设计：首次打开自动搜索，后续同文件+同源打开直接回显上次结果不上触网络。
/// </summary>
internal class PictureSearchForm : Form
{
    // 缓存 key = (文件路径, 源名称)
    private sealed class SearchCacheKey : IEquatable<SearchCacheKey>
    {
        public string FilePath { get; }
        public string Source { get; }
        public SearchCacheKey(string filePath, string source) { FilePath = filePath; Source = source; }
        public bool Equals(SearchCacheKey? other) => other != null && FilePath == other.FilePath && Source == other.Source;
        public override bool Equals(object? obj) => Equals(obj as SearchCacheKey);
        public override int GetHashCode() => (FilePath?.GetHashCode() ?? 0) ^ (Source?.GetHashCode() ?? 0);
    }

    /// <summary>缓存的搜索结果：搜索结果列表 + 每个结果的缩略图字节</summary>
    private class CachedSearchData
    {
        public string Query { get; set; } = "";
        public List<SearchResult> Results { get; set; } = new();
        public int SearchLimit { get; set; }
        public int SearchOffset { get; set; }
        /// <summary>与 Results 索引一一对应，null 表示该结果没有缩略图</summary>
        public List<byte[]?> ThumbnailData { get; set; } = new();
    }

    private static readonly Dictionary<SearchCacheKey, CachedSearchData> _searchCache = new();

    private readonly MusicFile _file;
    private readonly string _source;
    private readonly ICoverService _coverService;
    private readonly ISettingsService _settings;
    private readonly IImageCache _imageCache;

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
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _openCoverMI;
    private readonly ToolStripMenuItem _extractCoverMI;

    // 状态
    private List<SearchResult> _results = new();
    private CancellationTokenSource? _searchCts;
    private int _searchLimit;
    private int _searchOffset;
    private int _thumbnailsLoaded;
    private readonly SemaphoreSlim _thumbnailThrottle = new(10, 10);
    /// <summary>加载过程中暂存每个 result 的缩略图字节，搜索完成后写入缓存</summary>
    private readonly Dictionary<int, byte[]?> _pendingThumbnails = new();

    /// <summary>用户选中的封面（DialogResult == OK 时有值）</summary>
    public CoverArt? SelectedCover { get; private set; }

    /// <summary>用户选中的搜索结果</summary>
    public SearchResult? SelectedResult { get; private set; }

    public PictureSearchForm(MusicFile file, string source,
        ICoverService coverService, ISettingsService settings, IImageCache imageCache)
    {
        _file = file;
        _source = source;
        _coverService = coverService;
        _settings = settings;
        _imageCache = imageCache;

        Text = $"搜索封面 - {GetSourceDisplayName()} - {_file.Title} | {_file.Artist}";
        Size = new Size(660, 620);
        MinimumSize = new Size(460, 400);
        StartPosition = FormStartPosition.CenterParent;
        ShowIcon = false;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.White;

        // ---- 上方搜索栏 ----
        var query = SearchCondition.FromSettings(_settings).BuildSearchQuery(_file);

        _searchBox = new TextBox
        {
            Text = query,
            Location = new Point(12, 12),
            Width = 400,
            Font = new Font("Microsoft YaHei UI", 10F),
            BorderStyle = BorderStyle.FixedSingle,
        };

        _searchBtn = new Button
        {
            Text = "搜索",
            Location = new Point(418, 10),
            Width = 70,
            Height = 26,
            FlatStyle = FlatStyle.Standard,
        };
        _searchBtn.Click += OnSearchClick;

        _sourceLabel = new Label
        {
            Text = "",
            Location = new Point(0, 0),
            Size = Size.Empty,
            Visible = false,
        };

        // ---- 进度条 ----
        _loadingBar = new ProgressBar
        {
            Location = new Point(12, 46),
            Width = 620,
            Height = 6,
            Style = ProgressBarStyle.Marquee,
            Visible = false,
        };

        // ---- 结果列表 (LargeIcon) ----
        _thumbnailList = new ImageList
        {
            ImageSize = new Size(128, 128),
            ColorDepth = ColorDepth.Depth32Bit,
        };

        _resultView = new ListView
        {
            Location = new Point(12, 56),
            Width = 620,
            Height = 460,
            View = View.LargeIcon,
            OwnerDraw = true,
            LargeImageList = _thumbnailList,
            MultiSelect = false,
            HideSelection = false,
            FullRowSelect = true,
            Font = new Font("Microsoft YaHei UI", 9F),
            BackColor = Color.White,
        };
        _resultView.SelectedIndexChanged += OnSelectionChanged;
        _resultView.DoubleClick += OnResultDoubleClick;
        _resultView.DrawItem += OnDrawItem;

        // ---- 右键菜单 ----
        _contextMenu = new ContextMenuStrip();
        _openCoverMI = new ToolStripMenuItem("打开封面");
        _openCoverMI.Click += OnOpenCoverClick;
        _extractCoverMI = new ToolStripMenuItem("提取封面...");
        _extractCoverMI.Click += OnExtractCoverClick;
        _contextMenu.Items.AddRange(new ToolStripItem[] { _openCoverMI, _extractCoverMI });
        _resultView.ContextMenuStrip = _contextMenu;

        // ---- 状态文字 ----
        _statusLabel = new Label
        {
            Text = "",
            Location = new Point(12, 520),
            Width = 360,
            Height = 24,
            ForeColor = Color.FromArgb(96, 98, 102),
        };

        _loadMoreBtn = new Button
        {
            Text = "加载更多",
            Location = new Point(380, 518),
            Width = 96,
            Height = 30,
            Visible = _coverService.SupportsPagination(_source),
            Enabled = false,
            FlatStyle = FlatStyle.Standard,
        };
        _loadMoreBtn.Click += OnLoadMoreClick;

        // ---- 底部按钮 ----
        _okBtn = new Button
        {
            Text = "确定",
            Location = new Point(485, 518),
            Width = 70,
            Height = 30,
            Enabled = false,
            FlatStyle = FlatStyle.Standard,
        };
        _okBtn.Click += OnOkClick;

        _cancelBtn = new Button
        {
            Text = "取消",
            Location = new Point(562, 518),
            Width = 70,
            Height = 30,
            FlatStyle = FlatStyle.Standard,
        };
        _cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] {
            _searchBox, _searchBtn, _sourceLabel,
            _loadingBar, _resultView,
            _statusLabel, _loadMoreBtn, _okBtn, _cancelBtn
        });

        // 有缓存 → 直接回显；无缓存 → 自动搜索
        Load += (_, _) =>
        {
            var key = new SearchCacheKey(_file.FilePath, _source);
            if (_searchCache.TryGetValue(key, out var cached) && cached.Results.Count > 0)
            {
                _searchBox.Text = cached.Query;
                RestoreFromCache(cached);
            }
            else
            {
                _ = PerformSearchAsync(false);
            }
        };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _thumbnailThrottle.Dispose();

        // 关闭时把本次结果存入缓存（仅 DialogResult.OK 不做缓存 — 用户已经用了这个结果）
        if (DialogResult != DialogResult.OK && _results.Count > 0)
        {
            SaveToCache();
        }

        base.OnFormClosing(e);
    }

    /// <summary>从缓存回显搜索结果和缩略图</summary>
    private void RestoreFromCache(CachedSearchData cached)
    {
        _results = cached.Results.ToList();
        _searchLimit = cached.SearchLimit > 0
            ? cached.SearchLimit : Math.Max(1, _settings.WebSearchItemsLimit);
        _searchOffset = cached.SearchOffset > 0 ? cached.SearchOffset : _results.Count;

        if (_results.Count == 0)
        {
            _statusLabel.Text = "未找到匹配的封面";
            return;
        }

        // 重建缩略图 ImageList
        _resultView.BeginUpdate();
        _thumbnailList.Images.Clear();
        _resultView.Items.Clear();

        for (int i = 0; i < _results.Count; i++)
        {
            var result = _results[i];
            var thumbnailData = i < cached.ThumbnailData.Count ? cached.ThumbnailData[i] : null;
            var (thumb, width, height) = IconHelper.CreateThumbnailWithResolution(thumbnailData, 128);

            var item = new ListViewItem(BuildDisplayText(result, width, height))
            {
                Tag = result,
                ImageIndex = -1,
            };
            _resultView.Items.Add(item);

            // 有缓存缩略图则直接恢复
            if (thumb != null)
            {
                var key = $"cache_{i}";
                _thumbnailList.Images.Add(key, thumb);
                item.ImageKey = key;
            }
        }

        _resultView.EndUpdate();
        _statusLabel.Text = $"找到 {_results.Count} 个结果（缓存）";
        _loadingBar.Visible = false;
        _loadMoreBtn.Enabled = _loadMoreBtn.Visible;
    }

    /// <summary>将当前结果和缩略图写入静态缓存</summary>
    private void SaveToCache()
    {
        var key = new SearchCacheKey(_file.FilePath, _source);
        var thumbData = new List<byte[]?>();
        for (int i = 0; i < _results.Count; i++)
        {
            if (_pendingThumbnails.TryGetValue(i, out var data))
                thumbData.Add(data);
            else
                thumbData.Add(null);
        }

        _searchCache[key] = new CachedSearchData
        {
            Query = _searchBox.Text,
            Results = _results.ToList(),
            SearchLimit = _searchLimit,
            SearchOffset = _searchOffset,
            ThumbnailData = thumbData,
        };
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

        // 进入加载状态
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
                ItunesCountry = _settings.ItunesSearchParamsCountry ?? "US",
                CustomQuery = query,
            };

            var results = await _coverService.SearchCoversFromSourceAsync(
                _file, _source, condition, ct);

            if (ct.IsCancellationRequested) return;

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
                _statusLabel.Text = "未找到匹配的封面";
                return;
            }

            _statusLabel.Text = $"找到 {_results.Count} 个结果，正在加载缩略图...";
            _loadMoreBtn.Enabled = _loadMoreBtn.Visible && results.Count >= requestLimit;
            PopulateResults();
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
        _pendingThumbnails.Clear();

        _resultView.BeginUpdate();
        _resultView.Items.Clear();

        foreach (var result in _results)
        {
            var item = new ListViewItem(BuildDisplayText(result, 0, 0))
            {
                Tag = result,
                ImageIndex = -1, // 缩略图加载后更新
            };
            _resultView.Items.Add(item);
        }

        _resultView.EndUpdate();

        // 异步加载缩略图
        _thumbnailsLoaded = 0;
        foreach (var item in _resultView.Items.Cast<ListViewItem>())
        {
            var result = item.Tag as SearchResult;
            if (result != null)
            {
                int idx = item.Index;
                _ = LoadThumbnailAsync(item, result, idx);
            }
        }
    }

    private async Task LoadThumbnailAsync(ListViewItem item, SearchResult result, int resultIndex)
    {
        await _thumbnailThrottle.WaitAsync();
        try
        {
            if (string.IsNullOrEmpty(result.CoverUrl)) return;

            // 酷我的 CoverUrl 是 pic API 端点，需要先解析获取实际图片 URL
            var src = result.SourceName?.ToLowerInvariant() ?? "";
            var imageUrl = result.CoverUrl;
            if ((src.Contains("kuwo") || src.Contains("酷我")) && result.CoverUrl.Contains("artistpicserver.kuwo.cn"))
            {
                using var resolveHttp = _coverService.CreateHttpClientForSource(result.SourceName ?? "");
                resolveHttp.Timeout = TimeSpan.FromSeconds(10);
                imageUrl = await ResolveImageUrlAsync(result.CoverUrl, result.SourceName, resolveHttp);
                if (string.IsNullOrEmpty(imageUrl)) return;
            }

            // 使用带缓存的下载
            var data = await _coverService.DownloadImageBytesAsync(imageUrl, result.SourceName ?? "",
                _searchCts?.Token ?? CancellationToken.None);
            if (data == null) return;

            // 存缩略图字节用于缓存
            _pendingThumbnails[resultIndex] = data;
            var (thumb, width, height) = IconHelper.CreateThumbnailWithResolution(data, 128);
            if (thumb == null) return;

            // 回到 UI 线程更新
            BeginInvoke(() =>
            {
                if (_resultView.IsDisposed) { thumb.Dispose(); return; }

                var key = Guid.NewGuid().ToString();
                _thumbnailList.Images.Add(key, thumb);
                // item 可能已被复用，检查 Tag
                if (item.ListView != null && item.Tag == result)
                {
                    item.ImageKey = key;
                    item.Text = BuildDisplayText(result, width, height);
                }

                _thumbnailsLoaded++;
                _statusLabel.Text = $"找到 {_results.Count} 个结果（已加载 {_thumbnailsLoaded} 张缩略图）";

                // 全部加载完毕 → 写入缓存
                if (_thumbnailsLoaded >= _results.Count)
                    SaveToCache();
            });
        }
        catch { /* 单个缩略图加载失败不影响其他 */ }
        finally
        {
            _thumbnailThrottle.Release();
        }
    }

    private static string BuildDisplayText(SearchResult result, int width, int height)
    {
        var displayText = string.IsNullOrEmpty(result.Artist)
            ? result.Title ?? "未知"
            : $"{result.Artist} - {result.Title}";
        if (displayText.Length > 40)
            displayText = displayText.Substring(0, 37) + "...";

        return width > 0 && height > 0
            ? $"{displayText}\n{width}×{height}"
            : displayText;
    }

    /// <summary>解析封面图片 URL，对酷我源将 pic.web 端点解析为实际图片地址</summary>
    private static async Task<string?> ResolveImageUrlAsync(string? coverUrl, string? sourceName, HttpClient http)
    {
        if (string.IsNullOrEmpty(coverUrl)) return null;

        var src = sourceName?.ToLowerInvariant() ?? "";
        if ((src.Contains("kuwo") || src.Contains("酷我")) && coverUrl.Contains("artistpicserver.kuwo.cn"))
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, coverUrl);
                request.Headers.Add("Referer", "https://kuwo.cn");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.3; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/68.0.3440.106 Safari/537.36");
                var response = await http.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync();
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

        return coverUrl;
    }

    /// <summary>酷我 pic API 返回实际图片 URL（如 http://...jpg），需要先解析再下载</summary>
    [Obsolete("Use ResolveImageUrlAsync instead")]
    private static async Task<string?> ResolveKuwoCoverUrlAsync(string picApiUrl, HttpClient http, CancellationToken ct)
    {
        return await ResolveImageUrlAsync(picApiUrl, "kuwo", http);
    }

    // ============================================================
    // 自定义绘制 — 多行文字（标题 + 分辨率）
    // ============================================================

    private void OnDrawItem(object? sender, DrawListViewItemEventArgs e)
    {
        e.DrawBackground();
        e.DrawFocusRectangle();

        var bounds = e.Bounds;
        var item = e.Item;
        var lines = item.Text.Split('\n');

        // 图标区域：居中绘制在 bounds 上半部分
        var imageHeight = Math.Min(128, bounds.Height * 2 / 3);
        var imageWidth = imageHeight;
        var imageX = bounds.X + (bounds.Width - imageWidth) / 2;
        var imageY = bounds.Y + 2;
        if (item.ImageList != null)
        {
            Image? img = null;
            // 缩略图通过 ImageKey 添加，OwnerDraw 模式下 ImageIndex 不会被自动解析
            if (!string.IsNullOrEmpty(item.ImageKey) && item.ImageList.Images.ContainsKey(item.ImageKey))
                img = item.ImageList.Images[item.ImageKey];
            else if (item.ImageIndex >= 0 && item.ImageIndex < item.ImageList.Images.Count)
                img = item.ImageList.Images[item.ImageIndex];
            if (img != null)
                e.Graphics.DrawImage(img, imageX, imageY, imageWidth, imageHeight);
        }

        // 文字区域：图标下方，最多两行
        var textTop = imageY + imageHeight + 4;
        var textHeight = bounds.Bottom - textTop;
        if (textHeight < 1) return;

        var titleFont = item.ListView.Font;
        var smallFont = new Font(item.ListView.Font.FontFamily, 7.5f);

        using (var titleBrush = new SolidBrush(Color.Black))
        using (var subBrush = new SolidBrush(Color.Black))
        {
            var textRect = new Rectangle(bounds.X + 2, textTop, bounds.Width - 4, textHeight);
            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Near,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
            };

            // 标题行
            var titleRect = new Rectangle(textRect.X, textRect.Y, textRect.Width, titleFont.Height);
            e.Graphics.DrawString(lines[0], titleFont, titleBrush, titleRect, format);

            // 分辨率行
            if (lines.Length > 1)
            {
                var subRect = new Rectangle(textRect.X, titleRect.Bottom, textRect.Width, smallFont.Height);
                e.Graphics.DrawString(lines[1], smallFont, subBrush, subRect, format);
            }
        }

        smallFont.Dispose();
    }

    // ============================================================
    // 选择 / 确定
    // ============================================================

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        _okBtn.Enabled = _resultView.SelectedItems.Count > 0;
    }

    private void OnResultDoubleClick(object? sender, EventArgs e)
    {
        if (_resultView.SelectedItems.Count > 0)
            _ = ApplySelectedCoverAsync();
    }

    private async void OnOkClick(object? sender, EventArgs e)
    {
        await ApplySelectedCoverAsync();
    }

    private async Task ApplySelectedCoverAsync()
    {
        if (_resultView.SelectedItems.Count == 0) return;
        var item = _resultView.SelectedItems[0];
        var result = item.Tag as SearchResult;
        if (result == null) return;

        _loadingBar.Visible = true;
        _statusLabel.Text = "正在下载封面...";

        try
        {
            var limits = new CoverArt.LimitsConfig
            {
                FormatLimits = _settings.PictureFormatLimits ?? "jpg,jpeg,png,bmp,gif",
                MaxResolution = _settings.PictureResolutionLimits,
                MaxSizeKB = _settings.PictureSizeLimitsKB,
            };

            var cover = await _coverService.DownloadCoverAsync(result, limits,
                _searchCts?.Token ?? CancellationToken.None);

            if (cover == null)
            {
                _statusLabel.Text = "下载失败或图片不符合限制条件";
                return;
            }

            SelectedCover = cover;
            SelectedResult = result;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"下载失败：{ex.Message}";
        }
        finally
        {
            _loadingBar.Visible = false;
        }
    }

    // ============================================================
    // 右键菜单
    // ============================================================

    private async void OnOpenCoverClick(object? sender, EventArgs e)
    {
        var result = GetContextMenuResult();
        if (result == null) return;

        try
        {
            _statusLabel.Text = "正在下载...";
            using var http = _coverService.CreateHttpClientForSource(result.SourceName ?? "");
            http.Timeout = TimeSpan.FromSeconds(15);
            var imageUrl = await ResolveImageUrlAsync(result.CoverUrl, result.SourceName, http);
            if (string.IsNullOrEmpty(imageUrl))
            {
                _statusLabel.Text = "无法解析封面地址";
                return;
            }
            var data = await _coverService.DownloadImageBytesAsync(imageUrl, result.SourceName ?? "",
                CancellationToken.None);
            if (data == null || data.Length == 0)
            {
                _statusLabel.Text = "下载失败";
                return;
            }
            var rel = _imageCache.StoreHistory(data);
            if (string.IsNullOrEmpty(rel)) { _statusLabel.Text = "缓存失败"; return; }
            var full = _imageCache.GetHistoryFullPath(rel);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(full)
            {
                UseShellExecute = true
            });
            _statusLabel.Text = "";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"打开失败：{ex.Message}";
        }
    }

    private async void OnExtractCoverClick(object? sender, EventArgs e)
    {
        var result = GetContextMenuResult();
        if (result == null) return;

        using var dlg = new SaveFileDialog
        {
            Title = "保存封面图片",
            Filter = "JPEG 图片|*.jpg|PNG 图片|*.png|所有文件|*.*",
            FileName = $"{_file.Artist} - {_file.Title}_cover.jpg",
            RestoreDirectory = true,
        };

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _statusLabel.Text = "正在下载...";
            using var http = _coverService.CreateHttpClientForSource(result.SourceName ?? "");
            http.Timeout = TimeSpan.FromSeconds(15);
            var imageUrl = await ResolveImageUrlAsync(result.CoverUrl, result.SourceName, http);
            if (string.IsNullOrEmpty(imageUrl))
            {
                _statusLabel.Text = "无法解析封面地址";
                return;
            }
            var data = await http.GetByteArrayAsync(imageUrl);
            File.WriteAllBytes(dlg.FileName, data);
            _statusLabel.Text = $"已保存到：{dlg.FileName}";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"保存失败：{ex.Message}";
        }
    }

    private SearchResult? GetContextMenuResult()
    {
        if (_resultView.SelectedItems.Count == 0) return null;
        return _resultView.SelectedItems[0].Tag as SearchResult;
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
