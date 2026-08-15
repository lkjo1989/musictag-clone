using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MusicTagClone.Controls;
using MusicTagClone.Interfaces;
using MusicTagClone.Models;
using MusicTagClone.Services;
using MusicTagClone.Win32;
using MusicTagClone.ChineseUtils;
using Newtonsoft.Json;

namespace MusicTagClone.Forms;

/// <summary>
/// 主窗体 — 左侧标签编辑面板(含封面/歌词)，右侧文件列表，底部过滤/状态栏
/// </summary>
public partial class MainForm : Form
{
    private readonly ISettingsService _settings;
    private readonly IFileScannerService _fileScanner;
    private readonly ITagService _tagService;
    private readonly ILyricService _lyricService;
    private readonly ICoverService _coverService;
    private readonly WebSearchService _webSearch;
    private readonly AutoMatchService _autoMatch;
    private readonly FilenameRelationService _filenameRelation;
    private readonly ITagHistoryService _tagHistory;
    private readonly ILoggerService _logger;
    private readonly IImageCache _imageCache;

    private List<MusicFile> _files = new();
    private MusicFile? _currentFile;
    private string _sortField = "FileName";
    private bool _sortAscending = true;
    private readonly HashSet<string> _hiddenDirs = new(StringComparer.OrdinalIgnoreCase);
    private List<ColumnHeaderInfo> _columnSettings = ColumnHeaderInfo.CreateDefaults();

    // 封面缩略图缓存 — LRU 策略
    // 只缓存显示尺寸（256px）的缩略图位图，不缓存全尺寸原图；切换文件时 UI 线程不解码大图。
    private static readonly Dictionary<string, Bitmap> CoverCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> CoverCacheOrder = new();
    private const int CoverCacheMaxSize = 80;       // 缩略图小，容量可放大
    private const int ThumbnailSize = 256;          // 2x of 170 显示尺寸，给 DPI/缩放留余量
    private CancellationTokenSource? _coverLoadCts;

    public MainForm(ISettingsService settings, IFileScannerService fileScanner,
        ITagService tagService, ILyricService lyricService, ICoverService coverService,
        WebSearchService webSearch, AutoMatchService autoMatch, FilenameRelationService filenameRelation,
        ITagHistoryService tagHistory,
        ILoggerService logger, IImageCache imageCache)
    {
        _settings = settings;
        _fileScanner = fileScanner;
        _tagService = tagService;
        _lyricService = lyricService;
        _coverService = coverService;
        _webSearch = webSearch;
        _autoMatch = autoMatch;
        _filenameRelation = filenameRelation;
        _tagHistory = tagHistory;
        _logger = logger;
        _imageCache = imageCache;

        InitializeComponent();
        RestoreWindowState();

        // 列宽变动防抖保存定时器
        _columnWidthSaveTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _columnWidthSaveTimer.Tick += (s, e) =>
        {
            _columnWidthSaveTimer.Stop();
            SaveColumnSettings();
        };

        // 延迟设置拖拽 — 句柄创建完成后才生效
        Load += OnMainFormLoad;
    }

    private void OnMainFormLoad(object? sender, EventArgs e)
    {
        _logger.Info("应用启动");

        // 初始化标签历史数据库
        _tagHistory.Initialize();

        // 在全部控件上递归开启拖放 — OLE 要求当前鼠标下的控件必须 AllowDrop=true
        SetupDropForControl(this);

        // 恢复上次打开的文件列表
        RestoreFileList();

        // 加载并应用列自定义设置
        LoadColumnSettings();
        ApplyColumnSettings();
    }

    /// <summary>
    /// 处理 WM_COPYDATA — 其他实例传递的文件路径。
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_COPYDATA)
        {
            var cds = Marshal.PtrToStructure<NativeMethods.COPYDATASTRUCT>(m.LParam);
            // 验证 dwData 标识，防止外部程序伪造
            if (cds.dwData == new IntPtr(0x4D546167) && !string.IsNullOrEmpty(cds.lpData))
            {
                var paths = cds.lpData.Split('\n')
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p.Trim())
                    .ToList();
                if (paths.Count > 0)
                {
                    LoadFilesFromPaths(paths);
                }
            }
            // 激活自身窗口
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            Activate();
        }
        base.WndProc(ref m);
    }

    /// <summary>
    /// 从路径列表加载文件（由命令行参数传递或 WM_COPYDATA 触发）。
    /// </summary>
    private void LoadFilesFromPaths(List<string> paths)
    {
        // 分离文件和目录
        var files = paths.Where(File.Exists)
            .Where(f => SupportedExts.Contains(Path.GetExtension(f)))
            .ToList();
        var dirs = paths.Where(Directory.Exists).ToList();

        if (files.Count == 0 && dirs.Count == 0) return;

        SetBusy(true, "正在加载文件...");
        try
        {
            var newFiles = files.Select(MusicFile.FromPath).ToList();
            foreach (var dir in dirs)
            {
                try
                {
                    newFiles.AddRange(
                        Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                        .Where(f => SupportedExts.Contains(Path.GetExtension(f)))
                        .Select(MusicFile.FromPath));
                }
                catch { /* 目录访问失败，跳过 */ }
            }

            if (newFiles.Count == 0) return;

            _files = newFiles;
            RefreshFileList();
            _ = EnrichFilesAsync(newFiles);
            statusLabel.Text = $"已加载 {_files.Count} 个文件";
        }
        finally
        {
            SetBusy(false, "");
        }
    }

    private void SetupDropForControl(Control parent)
    {
        parent.AllowDrop = true;
        parent.DragEnter += OnFormDragEnter;
        parent.DragDrop += OnFormDragDrop;
        foreach (Control child in parent.Controls)
            SetupDropForControl(child);
    }

    // ============================================================
    // 拖拽文件
    // ============================================================

    private static readonly HashSet<string> SupportedExts = new(StringComparer.OrdinalIgnoreCase)
    { ".mp3", ".flac", ".m4a", ".ogg", ".wma", ".wav", ".ape", ".wv", ".aac", ".aiff", ".dsf", ".dff" };

    private void OnFormDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Any(f => SupportedExts.Contains(Path.GetExtension(f))))
            {
                e.Effect = DragDropEffects.Copy;
                return;
            }
        }
        e.Effect = DragDropEffects.None;
    }

    private void OnFormDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        var audioFiles = paths.Where(p => SupportedExts.Contains(Path.GetExtension(p))).ToList();
        var dirs = paths.Where(Directory.Exists).ToList();

        if (audioFiles.Count == 0 && dirs.Count == 0) return;

        SetBusy(true, "正在加载文件...");
        try
        {
            var newFiles = audioFiles.Select(MusicFile.FromPath).ToList();
            foreach (var dir in dirs)
            {
                newFiles.AddRange(
                    Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                    .Where(f => SupportedExts.Contains(Path.GetExtension(f)))
                    .Select(MusicFile.FromPath));
            }
            _files.AddRange(newFiles);
            RefreshFileList();
            _ = EnrichFilesAsync(newFiles);
            statusLabel.Text = $"已添加 {newFiles.Count} 个文件{(dirs.Count > 0 ? $" (含 {dirs.Count} 个目录)" : "")}";
        }
        finally
        {
            SetBusy(false, "");
        }
    }

    // ============================================================
    // 文件列表事件
    // ============================================================

    private void OnOpenDirectory(object? sender, EventArgs e)
    {
        // 工具栏"添加目录"按钮与菜单"添加目录..."行为一致
        OnAddDirectory(sender, e);
    }

    private void OnOpenFiles(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "添加音乐文件",
            Filter = "音频文件|*.mp3;*.flac;*.m4a;*.ogg;*.wma;*.wav;*.ape|所有文件|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            var newFiles = dlg.FileNames
                .Where(f => _fileScanner.IsSupportedFile(f))
                .Select(MusicFile.FromPath)
                .ToList();
            _files.AddRange(newFiles);
            RefreshFileList();
            _ = EnrichFilesAsync(newFiles);
        }
    }

    private async Task ScanAndLoadFilesAsync(string directory)
    {
        SetBusy(true, "正在扫描文件...");
        try
        {
            progressBar.Visible = true;
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            var progress = new Progress<int>(p =>
            {
                progressBar.Value = Math.Min(p, 100);
            });

            _files = (await _fileScanner.ScanDirectoryAsync(
                directory, _settings.IncludeSubDir, progress)).ToList();
            RefreshFileList();
        }
        finally
        {
            SetBusy(false, "");
        }
    }

    private async void OnFileListViewSelectedIndexChanged(object? sender, EventArgs e)
    {
        try
        {
            var selected = GetSelectedFiles();

            if (selected.Count == 0)
            {
                _currentFile = null;
                ClearSidebar();
            }
            else if (selected.Count == 1)
            {
                var mf = selected[0];
                _currentFile = mf;
                mf.IsSelected = true;

                // 选择文件时强制重新从磁盘读取标签（不缓存）
                mf.CoverArtData = null;
                mf.HasCoverArt = false;
                mf.Lyrics = null;
                mf.HasLyrics = false;
                await LoadTagsFromFile(mf);

                // 就地更新文件列表中该行的标签值（不重建全表）
                fileListView.BeginUpdate();
                UpdateFileListItem(mf);
                fileListView.EndUpdate();
            }
            else
            {
                // 多选模式：左侧栏所有字段显示 &lt;keep&gt;
                _currentFile = null;
                tagEditPanel.SetKeepMode();

                _coverLoadCts?.Cancel();
                _coverLoadCts?.Dispose();
                _coverLoadCts = null;
                SetCoverImage(null);
            }
            UpdateStatusBar();
            RefreshMenuStates();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "选择文件时出错");
        }
    }

    private void OnFileListViewColumnClick(object? sender, ColumnClickEventArgs e)
    {
        var field = e.Column switch
        {
            0 => "FileName",
            1 => "Directory",
            2 => "AudioFormat",
            3 => "Title",
            4 => "Artist",
            5 => "Album",
            6 => "AlbumArtist",
            7 => "Year",
            8 => "Track",
            9 => "Disc",
            10 => "Genre",
            11 => "Composer",
            12 => "Lyricist",
            13 => "Comment",
            14 => "HasCoverArt",
            15 => "Lyrics",
            16 => "Channels",
            17 => "SampleRate",
            18 => "BitRate",
            19 => "BitsPerSample",
            20 => "Duration",
            21 => "LastModified",
            _ => "FileName"
        };

        if (_sortField == field)
            _sortAscending = !_sortAscending;
        else
        {
            _sortField = field;
            _sortAscending = true;
        }

        ApplySort();
    }

    private void OnFileListViewMouseDown(object? sender, MouseEventArgs e)
    {
        var hitInfo = fileListView.HitTest(e.X, e.Y);
        if (hitInfo.Item == null)
        {
            fileListView.SelectedItems.Clear();
            // ClearSidebar 由 SelectedIndexChanged 事件触发
        }
    }

    // ============================================================
    // 标签编辑
    // ============================================================

    private void LoadFileToEditor(MusicFile file)
    {
        tagEditPanel.LoadFromFile(file);
        tagEditPanel.LoadPictures(file.AllPictures, file.CurrentPictureIndex);
        LoadCoverImage(file);
    }

    /// <summary>
    /// 载入封面：缓存命中则同步、瞬时显示；未命中则后台线程生成缩略图，完成后回 UI 线程赋值。
    /// UI 线程绝不解码大图，避免切换文件卡顿。
    /// 所有权约定：缓存持有"规范缩略图"并独占其生命周期（淘汰/替换时 Dispose）；
    /// PictureBox 持有该缩略图的独立克隆副本，由 SetCoverImage 在切换时自行 Dispose。
    /// 这样缓存淘汰正在显示的缩略图也不会影响 PictureBox 显示——这正是之前
    /// "切换几次后图片不加载"的根因（缓存 Dispose 了 PictureBox 仍在引用的位图）。
    /// </summary>
    private void LoadCoverImage(MusicFile file)
    {
        _coverLoadCts?.Cancel();
        _coverLoadCts = null;

        if (file.CoverArtData == null || file.CoverArtData.Length == 0)
        {
            SetCoverImage(null);
            return;
        }

        string cacheKey = file.FilePath + ":" + file.CurrentPictureIndex;
        if (CoverCache.TryGetValue(cacheKey, out var cached) && cached != null)
        {
            TouchCoverCache(cacheKey);
            SetCoverImage(CloneForDisplay(cached));   // 克隆，与缓存所有权隔离
            return;
        }

        // 未命中：先清空避免停留 stale 图，然后后台解码缩略图
        SetCoverImage(null);

        var cts = new CancellationTokenSource();
        _coverLoadCts = cts;
        var token = cts.Token;

        string capturedKey = cacheKey;
        byte[] capturedData = file.CoverArtData;
        var capturedFile = file;
        int capturedPicIndex = file.CurrentPictureIndex;
        Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            return IconHelper.CreateThumbnail(capturedData, ThumbnailSize);
        }, token).ContinueWith(t =>
        {
            if (t.IsFaulted || t.IsCanceled || t.Result == null) return;

            var thumb = t.Result;
            if (_currentFile != capturedFile || _currentFile.CurrentPictureIndex != capturedPicIndex)
            {
                thumb.Dispose();
                return;
            }
            AddCoverCache(capturedKey, thumb);                // 进缓存，缓存独占所有权
            SetCoverImage(CloneForDisplay(thumb));            // PictureBox 拿独立克隆
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>把缩略图克隆一份交给编辑器；克隆的生命周期由编辑器(PictureBox)管理</summary>
    private static Image? CloneForDisplay(Image? src)
    {
        if (src == null) return null;
        try { return new Bitmap(src); }
        catch { return null; }
    }

    /// <summary>
    /// 赋值封面到编辑器。新值是 PictureBox 专属克隆，切换时把上一张克隆 Dispose 掉，
    /// 避免每次切换泄漏一张 256px 位图。不影响缓存里的规范缩略图。
    /// </summary>
    private void SetCoverImage(Image? image)
    {
        var old = tagEditPanel.CoverImage;
        if (ReferenceEquals(old, image)) return;
        tagEditPanel.CoverImage = image;
        old?.Dispose();
    }

    /// <summary>添加缩略图到 LRU 缓存，超出容量时淘汰最久未用的并 Dispose</summary>
    private static void AddCoverCache(string key, Bitmap cover)
    {
        // 已存在：移除旧条目并 Dispose 旧位图（即将被新位图取代）
        if (CoverCache.TryGetValue(key, out var old))
        {
            CoverCacheOrder.Remove(key);
            old?.Dispose();
        }

        // 淘汰最久未用的
        while (CoverCache.Count >= CoverCacheMaxSize && CoverCacheOrder.Count > 0)
        {
            var oldest = CoverCacheOrder.First!.Value;
            CoverCacheOrder.RemoveFirst();
            if (CoverCache.TryGetValue(oldest, out var evicted))
            {
                CoverCache.Remove(oldest);
                evicted?.Dispose();
            }
        }

        CoverCache[key] = cover;
        CoverCacheOrder.AddLast(key);
    }

    /// <summary>命中时移动节点到末尾（O(1)）保持 LRU 顺序</summary>
    private static void TouchCoverCache(string key)
    {
        CoverCacheOrder.Remove(key);
        CoverCacheOrder.AddLast(key);
    }

    /// <summary>移除某文件的所有封面缓存条目（支持带索引的 key），并 Dispose 位图</summary>
    private static void RemoveCoverCache(string filePath)
    {
        var keysToRemove = CoverCache.Keys
            .Where(k => string.Equals(k, filePath, StringComparison.OrdinalIgnoreCase)
                     || k.StartsWith(filePath + ":", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var key in keysToRemove)
        {
            CoverCacheOrder.Remove(key);
            if (CoverCache.TryGetValue(key, out var img))
            {
                CoverCache.Remove(key);
                img?.Dispose();
            }
        }
    }

    private async void OnSaveTags(object? sender, EventArgs e)
    {
        try
        {
            _logger.Info("OnSaveTags 被调用");

            // 保存前记录当前标签状态到历史
            RecordTagHistoryBeforeSave();

            var selected = GetSelectedFiles();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "请先选择要保存的文件", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 从编辑器收集修改
            if (_currentFile != null)
                tagEditPanel.SaveToFile(_currentFile);

            SetBusy(true, "正在保存标签...");
            try
            {
                var fileTags = selected.Select(f =>
                    new KeyValuePair<string, TagData>(f.FilePath, TagData.FromMusicFile(f))).ToList();

                progressBar.Visible = true;
                progressBar.Maximum = fileTags.Count;
                var progress = new Progress<int>(p => progressBar.Value = p);

                var count = await _tagService.WriteTagsBatchAsync(fileTags,
                    _settings.SaveTagsKeepUpdateTime, progress);

                // 同时保存 LRC
                if (_settings.SaveLrcWhileSaveTags)
                {
                    foreach (var f in selected.Where(f => !string.IsNullOrEmpty(f.Lyrics)))
                    {
                        var lyric = new LyricInfo { OriginalLyric = f.Lyrics, LrcFormatted = f.Lyrics };
                        await _lyricService.SaveLrcFileAsync(f.Directory, f, lyric,
                            new LyricInfo.SaveConfig
                            {
                                SaveDirectory = _settings.SaveLrcDirectory ?? f.Directory,
                                FilenameFormat = _settings.SaveLrcFilenameFormat ?? "{artist} - {title}.lrc",
                                FileDefaultEncoding = _settings.SaveLrcFileDefaultEncoding ?? "utf-8"
                            });
                    }
                }

                // 保存成功后清除字段的"(已修改)"标记
                tagEditPanel.ResetFieldLabels();

                // 重新从磁盘读取标签，刷新左侧栏和文件列表
                await ReloadSavedFilesTagsAsync(selected);

                _logger.Info("保存标签完成：{0}/{1} 个文件", count, selected.Count);
                if (count == 0)
                {
                    MessageBox.Show(this, "保存失败：未能写入任何文件，请查看日志了解详情", "保存失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "OnSaveTags 保存过程中出错");
                MessageBox.Show(this, $"保存失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false, "");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "OnSaveTags 整体异常");
        }
    }

    /// <summary>保存后重新读取已保存文件的标签，刷新左侧栏和文件列表</summary>
    private async Task ReloadSavedFilesTagsAsync(List<MusicFile> savedFiles)
    {
        // 清除封面缓存，使重读时重新解码
        foreach (var f in savedFiles)
            RemoveCoverCache(f.FilePath);

        foreach (var f in savedFiles)
        {
            var tags = await _tagService.ReadTagsAsync(f.FilePath);
            ApplyTagsToFile(f, tags);
        }

        // 刷新文件列表行
        fileListView.BeginUpdate();
        foreach (var f in savedFiles)
            UpdateFileListItem(f);
        fileListView.EndUpdate();

        // 如果当前有文件选中且是被保存的文件之一，重新加载编辑器
        if (_currentFile != null && savedFiles.Contains(_currentFile))
        {
            // 数据已通过 ApplyTagsToFile 更新到 _currentFile，只需刷新编辑器
            _coverLoadCts?.Cancel();
            _coverLoadCts?.Dispose();
            _coverLoadCts = null;
            SetCoverImage(null);
            LoadFileToEditor(_currentFile);
        }
    }

    /// <summary>将 TagData 的值写回到 MusicFile 对象</summary>
    private static void ApplyTagsToFile(MusicFile file, TagData tags)
    {
        if (tags.Title != null) file.Title = tags.Title;
        if (tags.Artist != null) file.Artist = tags.Artist;
        if (tags.Album != null) file.Album = tags.Album;
        file.Year = tags.Year;
        file.Track = tags.Track;
        file.TrackCount = tags.TrackCount ?? 0;
        file.Disc = tags.Disc;
        file.DiscCount = tags.DiscCount ?? 0;
        if (tags.Genre != null) file.Genre = tags.Genre;
        if (tags.Comment != null) file.Comment = tags.Comment;
        if (tags.AlbumArtist != null) file.AlbumArtist = tags.AlbumArtist;
        if (tags.Composer != null) file.Composer = tags.Composer;
        if (tags.Lyricist != null) file.Lyricist = tags.Lyricist;
        if (tags.Lyrics != null) { file.Lyrics = tags.Lyrics; file.HasLyrics = true; }
        if (tags.CoverArtData != null && tags.CoverArtData.Length > 0)
        {
            file.HasCoverArt = true;
            file.CoverArtMimeType = tags.CoverArtMimeType ?? "image/jpeg";
            file.CoverArtData = tags.CoverArtData;
            file.CoverArtType = tags.CoverArtType;
            file.AllPictures = tags.AllPictures;
        }
        // 音频属性
        if (tags.DurationMs.HasValue)
            file.Duration = TimeSpan.FromMilliseconds(tags.DurationMs.Value);
        if (tags.BitRate.HasValue) file.BitRate = tags.BitRate.Value;
        if (tags.SampleRate.HasValue) file.SampleRate = tags.SampleRate.Value;
        if (tags.Channels.HasValue) file.Channels = tags.Channels.Value;
        if (tags.BitsPerSample.HasValue) file.BitsPerSample = tags.BitsPerSample.Value;
        if (tags.TagFormat != null) file.TagFormat = tags.TagFormat;
        file.IsModified = false;
    }

    // ============================================================
    // 文件(F) 菜单处理器
    // ============================================================

    private void OnChangeWorkDir(object? sender, EventArgs e)
    {
        try
        {
            var picker = new FolderPicker
            {
                Title = "选择工作目录",
                AllowMultiSelect = false,
                IncludeSubDirectories = true,
                CheckBoxLabel = "包含子文件"
            };
            if (!picker.ShowDialog(this))
                return;
            _ = ScanAndLoadFilesAsync(picker.SelectedPaths[0]);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "OnChangeWorkDir 失败");
            MessageBox.Show(this, $"操作失败：{ex.Message}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnAddDirectory(object? sender, EventArgs e)
    {
        var picker = new FolderPicker
        {
            Title = "选择要添加的音乐目录",
            AllowMultiSelect = true,
            IncludeSubDirectories = true,
            CheckBoxLabel = "包含子文件夹",
        };

        if (!picker.ShowDialog(this))
            return;

        SetBusy(true, "正在添加文件...");
        try
        {
            progressBar.Visible = true;
            int totalFiles = 0;
            foreach (var dir in picker.SelectedPaths)
            {
                int count = await AppendFilesFromDirectory(dir, picker.IncludeSubDirectoriesChecked);
                totalFiles += count;
            }
            statusLabel.Text = $"已添加 {totalFiles} 个文件 (共 {_files.Count} 个)";
        }
        finally
        {
            SetBusy(false, "");
        }
    }

    /// <summary>追加扫描目录到现有列表（不清空已有文件）</summary>
    private async Task<int> AppendFilesFromDirectory(string directory, bool includeSubDir = true)
    {
        try
        {
            var progress = new Progress<int>(p => { });
            var newFiles = (await _fileScanner.ScanDirectoryAsync(
                directory, includeSubDir, progress)).ToList();
            _files.AddRange(newFiles);
            RefreshFileList();
            return newFiles.Count;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"扫描目录失败：{ex.Message}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 0;
        }
    }

    private void OnManageDirectory(object? sender, EventArgs e)
    {
        if (_files.Count == 0)
        {
            MessageBox.Show(this, "没有已添加的目录", "管理目录", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var dirs = _files.Select(f => f.Directory).Distinct().OrderBy(d => d).ToList();
        if (dirs.Count == 0) return;

        var dlg = new Form
        {
            Text = "管理目录",
            Size = new Size(500, 420),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var label = new Label
        {
            Text = "勾选要显示的目录，未勾选的目录暂时隐藏：",
            Location = new Point(12, 12),
            Width = 460,
            Height = 30
        };

        var checkedList = new CheckedListBox
        {
            Location = new Point(12, 44),
            Width = 460,
            Height = 280,
            CheckOnClick = true,
            Font = new Font("Microsoft YaHei UI", 9.5F)
        };
        foreach (var d in dirs)
            checkedList.Items.Add(d, !_hiddenDirs.Contains(d));

        var okBtn = new Button { Text = "确定", Location = new Point(290, 340), Width = 80 };
        var cancelBtn = new Button { Text = "取消", Location = new Point(380, 340), Width = 80 };

        okBtn.Click += (s2, e2) =>
        {
            _hiddenDirs.Clear();
            for (int i = 0; i < checkedList.Items.Count; i++)
                if (!checkedList.GetItemChecked(i))
                    _hiddenDirs.Add(checkedList.Items[i].ToString()!);

            _currentFile = null;
            tagEditPanel.LoadFromFile(new MusicFile());
            RefreshFileList();
            statusLabel.Text = $"显示 {_files.Count - _hiddenDirs.Count}/{_files.Count} 个文件";
            dlg.Close();
        };
        cancelBtn.Click += (s2, e2) => dlg.Close();

        dlg.Controls.AddRange(new Control[] { label, checkedList, okBtn, cancelBtn });
        dlg.ShowDialog(this);
    }

    private void OnClearTags(object? sender, EventArgs e)
    {
        try
        {
            if (_currentFile == null) return;

            // 清除前先把原始标签记入历史，否则清空 _currentFile 后保存时的快照只剩空值，无法还原
            if (HasAnyTagValue(_currentFile))
                _tagHistory.TryAddHistory(_currentFile.FilePath, _currentFile);

            // 直接清空左侧栏所有字段内容 — 像手动一个一个清空一样等待保存
            tagEditPanel.SetFieldText("title", "");
            tagEditPanel.SetFieldText("artist", "");
            tagEditPanel.SetFieldText("album", "");
            tagEditPanel.SetFieldText("year", "");
            tagEditPanel.SetFieldText("track", "");     // 补漏：清空音轨号
            tagEditPanel.SetFieldText("disc", "");      // 补漏：清空碟号
            tagEditPanel.SetFieldText("genre", "");
            tagEditPanel.SetFieldText("albumartist", "");
            tagEditPanel.SetFieldText("composer", "");
            tagEditPanel.SetFieldText("lyricist", "");
            tagEditPanel.SetFieldText("comment", "");
            tagEditPanel.UpdateLyrics("");

            // 同步清空当前文件对象，使文件列表行立即刷新为清空状态（否则行不刷新，仍显示旧值）
            _currentFile.Title = "";
            _currentFile.Artist = "";
            _currentFile.Album = "";
            _currentFile.Year = 0;
            _currentFile.Track = 0;
            _currentFile.TrackCount = 0;
            _currentFile.Disc = 0;
            _currentFile.DiscCount = 0;
            _currentFile.Genre = "";
            _currentFile.AlbumArtist = "";
            _currentFile.Composer = "";
            _currentFile.Lyricist = "";
            _currentFile.Comment = "";
            _currentFile.Lyrics = null;
            _currentFile.HasLyrics = false;
            _currentFile.IsModified = true;

            // 清空文件列表当前行的显示
            fileListView.BeginUpdate();
            UpdateFileListItem(_currentFile);
            fileListView.EndUpdate();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "OnClearTags 失败");
            MessageBox.Show(this, $"操作失败：{ex.Message}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnReadTags(object? sender, EventArgs e)
    {
        try
        {
            if (_currentFile == null) return;
            // 强制重新从磁盘读取标签（绕过 CoverArtData 缓存检查）
            _currentFile.CoverArtData = null;
            _currentFile.HasCoverArt = false;
            _currentFile.Lyrics = null;
            _currentFile.HasLyrics = false;
            await LoadTagsFromFile(_currentFile);
            // 只更新当前文件在文件列表中的对应行（不重建全表）
            fileListView.BeginUpdate();
            UpdateFileListItem(_currentFile);
            fileListView.EndUpdate();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "OnReadTags 失败");
            MessageBox.Show(this, $"操作失败：{ex.Message}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnOpenCover(object? sender, EventArgs e)
    {
        if (_currentFile == null) return;

        using var dlg = new OpenFileDialog
        {
            Title = "选择封面图片",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif|所有文件|*.*"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            var cover = _coverService.LoadImageFromFile(dlg.FileName);
            if (cover == null) return;

            var limits = new CoverArt.LimitsConfig
            {
                FormatLimits = _settings.PictureFormatLimits ?? "jpg,jpeg,png,bmp,gif",
                MaxResolution = _settings.PictureResolutionLimits,
                MaxSizeKB = _settings.PictureSizeLimitsKB
            };

            if (!_coverService.ValidateCover(cover, limits, out var error))
            {
                MessageBox.Show(this, error, "封面验证失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            await _tagService.WriteCoverArtAsync(_currentFile.FilePath, cover);
            _currentFile.HasCoverArt = true;
            _currentFile.CoverArtData = cover.ImageData;

            // 根据覆盖标志决定替换还是追加
            if (tagEditPanel.OverwriteCover)
            {
                _currentFile.AllPictures = new List<CoverArt> { cover };
                _currentFile.CurrentPictureIndex = 0;
            }
            else
            {
                _currentFile.AllPictures ??= new List<CoverArt>();
                _currentFile.AllPictures.Add(cover);
                _currentFile.CurrentPictureIndex = _currentFile.AllPictures.Count - 1;
            }

            // 丢弃旧缩略图缓存条目（位图 Dispose 由 RemoveCoverCache 负责）
            RemoveCoverCache(_currentFile.FilePath);

            // 刷新导航 + 封面信息；缩略图由 LoadCoverImage 异步生成并存入缓存
            tagEditPanel.LoadPictures(_currentFile.AllPictures, _currentFile.CurrentPictureIndex);
            tagEditPanel.UpdateCoverInfo(_currentFile);
            LoadCoverImage(_currentFile);
        }
    }

    private async void OnCompressCover(object? sender, EventArgs e)
    {
        if (_currentFile == null || !_currentFile.HasCoverArt) return;

        var cover = await _tagService.ReadCoverArtAsync(_currentFile.FilePath);
        if (cover == null) return;

        var compressed = _coverService.CompressCover(cover);
        if (compressed == null) return;

        await _tagService.WriteCoverArtAsync(_currentFile.FilePath, compressed);
        _currentFile.CoverArtData = compressed.ImageData;
        RemoveCoverCache(_currentFile.FilePath);

        // 缩略图由 LoadCoverImage 异步重新生成并进缓存
        LoadCoverImage(_currentFile);
        tagEditPanel.UpdateCoverInfo(_currentFile);

        MessageBox.Show(this, $"封面已压缩: {cover.FileSizeBytes / 1024}KB → {compressed.FileSizeBytes / 1024}KB",
            "压缩完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async void OnDeleteCover(object? sender, EventArgs e)
    {
        if (_currentFile == null) return;

        var result = MessageBox.Show(this, "确定要删除封面图片吗？", "确认",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (result != DialogResult.OK) return;

        await _tagService.WriteCoverArtAsync(_currentFile.FilePath, new CoverArt());
        _currentFile.HasCoverArt = false;
        RemoveCoverCache(_currentFile.FilePath);
        _currentFile.CoverArtData = null;
        SetCoverImage(null);    // 清空 PictureBox 克隆（旧克隆由 SetCoverImage Dispose）

        // 从多图列表中移除当前图片
        if (_currentFile.AllPictures != null && _currentFile.AllPictures.Count > 0)
        {
            int idx = tagEditPanel.CurrentPictureIndex;
            if (idx >= 0 && idx < _currentFile.AllPictures.Count)
                _currentFile.AllPictures.RemoveAt(idx);

            if (_currentFile.AllPictures.Count > 0)
            {
                // 还有剩余图片
                _currentFile.CurrentPictureIndex = Math.Min(idx, _currentFile.AllPictures.Count - 1);
                _currentFile.HasCoverArt = true;
                _currentFile.CoverArtData = _currentFile.AllPictures[_currentFile.CurrentPictureIndex].ImageData;
                _currentFile.CoverArtMimeType = _currentFile.AllPictures[_currentFile.CurrentPictureIndex].MimeType;
                LoadFileToEditor(_currentFile);
            }
            else
            {
                // 已无图片：刷新导航状态与封面信息
                tagEditPanel.LoadPictures(_currentFile.AllPictures, 0);
                tagEditPanel.UpdateCoverInfo(_currentFile);
            }
        }
    }

    // ============================================================
    // 标签源(S) / 搜索
    // ============================================================

    private async void OnSearchLyricSource(string source)
    {
        if (_currentFile == null) return;

        using var dlg = new LyricsSearchForm(_currentFile, source, _lyricService, _settings);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var lyric = dlg.SelectedLyric;
        if (lyric?.OriginalLyric == null) return;

        // 应用歌词到当前文件
        _currentFile.Lyrics = lyric.OriginalLyric;
        _currentFile.HasLyrics = true;
        tagEditPanel.UpdateLyrics(lyric.OriginalLyric);

        // 刷新文件列表
        fileListView.BeginUpdate();
        UpdateFileListItem(_currentFile);
        fileListView.EndUpdate();

        _logger.Info("歌词搜索完成: 来源={0}, 文件={1}", source, _currentFile.FilePath);
    }
    private async void OnSearchPictureSource(string source)
    {
        if (_currentFile == null) return;

        using var dlg = new PictureSearchForm(_currentFile, source, _coverService, _settings, _imageCache);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var cover = dlg.SelectedCover;
        if (cover == null) return;

        // 验证
        var limits = new CoverArt.LimitsConfig
        {
            FormatLimits = _settings.PictureFormatLimits ?? "jpg,jpeg,png,bmp,gif",
            MaxResolution = _settings.PictureResolutionLimits,
            MaxSizeKB = _settings.PictureSizeLimitsKB,
        };

        if (!_coverService.ValidateCover(cover, limits, out var error))
        {
            MessageBox.Show(this, error, "封面验证失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 写入文件
        await _tagService.WriteCoverArtAsync(_currentFile.FilePath, cover);
        _currentFile.HasCoverArt = true;
        _currentFile.CoverArtData = cover.ImageData;
        _currentFile.CoverArtMimeType = cover.MimeType;

        // 根据覆盖标志决定替换还是追加
        if (tagEditPanel.OverwriteCover)
        {
            _currentFile.AllPictures = new List<CoverArt> { cover };
            _currentFile.CurrentPictureIndex = 0;
        }
        else
        {
            _currentFile.AllPictures ??= new List<CoverArt>();
            _currentFile.AllPictures.Add(cover);
            _currentFile.CurrentPictureIndex = _currentFile.AllPictures.Count - 1;
        }

        // 刷新 UI
        RemoveCoverCache(_currentFile.FilePath);
        tagEditPanel.LoadPictures(_currentFile.AllPictures, _currentFile.CurrentPictureIndex);
        tagEditPanel.UpdateCoverInfo(_currentFile);
        LoadCoverImage(_currentFile);

        _logger.Info("图片搜索完成: 来源={0}, 文件={1}", source, _currentFile.FilePath);
    }
    private async void OnSearchCombTagSource(string source)
    {
        if (_currentFile == null) return;

        using var dlg = new TagSearchForm(_currentFile, source, _coverService, _lyricService, _imageCache, _settings);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var result = dlg.SelectedResult;
        if (result == null) return;

        // 构建 TagData
        var tags = new TagData();
        if (!string.IsNullOrEmpty(result.Title))
            tags.Title = result.Title;
        if (!string.IsNullOrEmpty(result.Artist))
            tags.Artist = result.Artist;
        if (!string.IsNullOrEmpty(result.Album))
            tags.Album = result.Album;
        if (!string.IsNullOrEmpty(result.Year) && uint.TryParse(result.Year, out var year))
            tags.Year = year;
        if (result.ExtraFields.TryGetValue("track", out var trackStr) && uint.TryParse(trackStr, out var track))
            tags.Track = track;
        if (result.ExtraFields.TryGetValue("disc", out var discStr) && uint.TryParse(discStr, out var disc))
            tags.Disc = disc;

        // 从历史缓存目录读取封面（搜索时已下载并 StoreHistory，无需重复下载）
        CoverArt? cover = null;
        if (!string.IsNullOrEmpty(result.CoverTempPath) && _imageCache.HistoryExists(result.CoverTempPath))
        {
            try
            {
                var data = _imageCache.ReadHistory(result.CoverTempPath);
                if (data != null && data.Length > 0)
                {
                    var full = _imageCache.GetHistoryFullPath(result.CoverTempPath);
                    var ext = Path.GetExtension(full).ToLowerInvariant();
                    var mime = ext switch
                    {
                        ".png" => "image/png",
                        ".gif" => "image/gif",
                        ".bmp" => "image/bmp",
                        ".webp" => "image/webp",
                        _ => "image/jpeg"
                    };
                    cover = new CoverArt { ImageData = data, MimeType = mime };
                    using var ms = new MemoryStream(data);
                    using var img = Image.FromStream(ms);
                    cover.Width = img.Width;
                    cover.Height = img.Height;
                    tags.AllPictures = new List<CoverArt> { cover };
                }
            }
            catch { /* 封面读取失败不影响标签写入 */ }
        }

        // 一次性写入标签+封面（单次文件打开/保存，避免两次写入冲突）
        var keepTime = _settings.SaveTagsKeepUpdateTime;
        var writeOk = await _tagService.WriteTagsAsync(_currentFile.FilePath, tags, keepTime);
        _logger.Info("WriteTagsAsync 结果: {0}, AllPictures={1}", writeOk, tags.AllPictures?.Count ?? 0);

        // 更新内存中的文件对象
        if (tags.Title != null) _currentFile.Title = tags.Title;
        if (tags.Artist != null) _currentFile.Artist = tags.Artist;
        if (tags.Album != null) _currentFile.Album = tags.Album;
        if (tags.Year.HasValue) _currentFile.Year = tags.Year.Value;
        if (tags.Track.HasValue) _currentFile.Track = tags.Track.Value;
        if (tags.Disc.HasValue) _currentFile.Disc = tags.Disc.Value;

        // 更新封面显示
        if (cover != null)
        {
            _currentFile.HasCoverArt = true;
            _currentFile.CoverArtData = cover.ImageData;
            _currentFile.CoverArtMimeType = cover.MimeType;
            _currentFile.AllPictures = new List<CoverArt> { cover };
            _currentFile.CurrentPictureIndex = 0;

            _logger.Info("更新封面显示: HasCoverArt=true, DataLen={0}", cover.ImageData?.Length ?? 0);
            RemoveCoverCache(_currentFile.FilePath);
            tagEditPanel.LoadPictures(_currentFile.AllPictures, _currentFile.CurrentPictureIndex);
            tagEditPanel.UpdateCoverInfo(_currentFile);
            LoadCoverImage(_currentFile);
        }

        // 尝试下载歌词并写入
        try
        {
            var lyricCondition = new SearchCondition
            {
                CustomQuery = $"{result.Artist} {result.Title}",
                WebSearchItemsLimit = Math.Max(1, _settings.WebSearchItemsLimit)
            };
            var lyricConfig = new LyricInfo.DownloadConfig();
            var lyricResults = await _lyricService.SearchLyricsFromSourceAsync(
                _currentFile, source, lyricCondition, lyricConfig);
            if (lyricResults.Count > 0)
            {
                var lyric = await _lyricService.DownloadLyricAsync(lyricResults[0], lyricConfig);
                if (lyric?.OriginalLyric != null)
                {
                    _currentFile.Lyrics = lyric.OriginalLyric;
                    _currentFile.HasLyrics = true;
                    // 将歌词写入文件
                    var lyricTags = new TagData { Lyrics = lyric.OriginalLyric };
                    await _tagService.WriteTagsAsync(_currentFile.FilePath, lyricTags, keepTime);
                }
            }
        }
        catch { /* 歌词下载失败不影响标签写入 */ }

        // 刷新 UI
        tagEditPanel.UpdateFieldTexts(_currentFile);
        UpdateFileListItem(_currentFile);

        _logger.Info("组合标签搜索完成: 来源={0}, 文件={1}", source, _currentFile.FilePath);
    }

    // ============================================================
    // 编辑(E) 菜单处理器
    // ============================================================

    private void OnInvertSelect(object? sender, EventArgs e)
    {
        try
        {
            fileListView.BeginUpdate();
            foreach (ListViewItem item in fileListView.Items)
                item.Selected = !item.Selected;
            fileListView.EndUpdate();
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "OnInvertSelect 失败");
        }
    }

    private void OnRename(object? sender, EventArgs e)
    {
        if (_currentFile == null || fileListView.FocusedItem == null) return;
        // 触发 ListView 内联编辑（F2 快捷键已在菜单中绑定）
        fileListView.FocusedItem.BeginEdit();
    }

    /// <summary>ListView 内联重命名完成 — 执行实际的文件重命名操作</summary>
    private void OnFileAfterLabelEdit(object? sender, LabelEditEventArgs e)
    {
        // e.Label = null 表示用户取消了编辑（按 Esc）
        if (e.Label == null) { e.CancelEdit = false; return; }

        var newName = e.Label.Trim();
        if (string.IsNullOrEmpty(newName))
        {
            e.CancelEdit = true;
            return;
        }

        // 找到对应的 MusicFile
        if (e.Item < 0 || e.Item >= fileListView.Items.Count) return;
        var item = fileListView.Items[e.Item];
        var file = item.Tag as MusicFile;
        if (file == null || file != _currentFile) return;

        var oldName = file.FileName;
        if (newName == oldName) return;

        // 如果没有扩展名，补上原扩展名
        if (!Path.HasExtension(newName))
            newName += file.Extension;

        var dir = file.Directory;
        var newPath = Path.Combine(dir, newName);
        var oldPath = file.FilePath;  // 日志用

        try
        {
            File.Move(oldPath, newPath);
            file.FilePath = newPath;

            // 只更新这一行的文件名列（不刷新全表）
            item.Text = file.FileName;

            // 更新状态栏
            statusLabel.Text = $"已重命名: {oldName} → {file.FileName}";
            _logger.Info("重命名成功: {0} → {1}", oldPath, newPath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "重命名失败: {0} → {1}", file.FilePath, newPath);
            MessageBox.Show(this, $"重命名失败：{ex.Message}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            e.CancelEdit = true;
        }
    }

    private async void OnDeleteFiles(object? sender, EventArgs e)
    {
        var sel = GetSelectedFiles();
        if (sel.Count == 0) return;
        if (MessageBox.Show(this, $"确定要永久删除 {sel.Count} 个文件吗？此操作不可撤销！", "确认删除",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        await _fileScanner.DeleteFilesAsync(sel);
        foreach (var f in sel) _files.Remove(f);
        RefreshFileList();
    }

    private void OnOpenFileDirectory(object? sender, EventArgs e)
    {
        if (_currentFile == null) return;
        Process.Start("explorer.exe", $"/select,\"{_currentFile.FilePath}\"");
    }

    // ============================================================
    // 视图(V) 菜单处理器
    // ============================================================

    private void OnCustomizeColumns(object? sender, EventArgs e)
    {
        using var dialog = new ColumnSelectDialog(_columnSettings);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result != null)
        {
            _columnSettings = dialog.Result;
            SaveColumnSettings();
            ApplyColumnSettings();
        }
    }

    /// <summary>从设置中加载列配置，不存在则使用默认值</summary>
    private void LoadColumnSettings()
    {
        try
        {
            var json = _settings.ListviewColumnHeader;
            if (!string.IsNullOrEmpty(json))
            {
                var saved = JsonConvert.DeserializeObject<List<ColumnHeaderInfo>>(json);
                if (saved != null && saved.Count > 0)
                    _columnSettings = saved;
            }
        }
        catch (Exception ex) { _logger.Error(ex, "加载列配置失败，使用默认值"); }
    }

    /// <summary>将当前列配置保存到设置中</summary>
    private void SaveColumnSettings()
    {
        try
        {
            // 从实际的 ColumnHeader 对象同步当前宽度
            SyncColumnWidthsFromListView();
            _settings.ListviewColumnHeader = JsonConvert.SerializeObject(_columnSettings);
            _settings.Save();
        }
        catch (Exception ex) { _logger.Error(ex, "保存列配置失败"); }
    }

    /// <summary>将 ListView 中当前的列宽同步回 _columnSettings</summary>
    private void SyncColumnWidthsFromListView()
    {
        var colMap = GetColumnHeaderMap();
        foreach (var info in _columnSettings)
        {
            if (colMap.TryGetValue(info.Name, out var col))
                info.Width = col.Width;
        }
    }

    /// <summary>将列设置应用到 ListView（可见性 + 显示顺序）</summary>
    private void ApplyColumnSettings()
    {
        try
        {
            var colMap = GetColumnHeaderMap();

            // 1) 按配置设置列宽（隐藏列宽设为 0）
            foreach (var info in _columnSettings)
            {
                if (colMap.TryGetValue(info.Name, out var col))
                {
                    col.Width = info.IsShow ? info.Width : 0;
                }
            }

            // 2) 按 DisplayIndex 排序（仅控制顺序，不因可见性改变位置）
            var sorted = _columnSettings
                .OrderBy(c => c.DisplayIndex)
                .ToList();

            // 3) 依次设置 DisplayIndex（确保从 0 递增，不冲突）
            fileListView.BeginUpdate();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (colMap.TryGetValue(sorted[i].Name, out var col))
                    col.DisplayIndex = i;
            }

            // 最后一列宽度减 1 再加回，触发滚动条刷新
            if (fileListView.Columns.Count > 0)
            {
                var last = fileListView.Columns[fileListView.Columns.Count - 1];
                last.Width -= 1;
                last.Width += 1;
            }
            fileListView.EndUpdate();
        }
        catch (Exception ex) { _logger.Error(ex, "应用列配置失败"); }
    }

    /// <summary>列名 → ColumnHeader 映射</summary>
    private Dictionary<string, ColumnHeader> GetColumnHeaderMap()
    {
        return new Dictionary<string, ColumnHeader>
        {
            ["filename"] = colFileName,
            ["filedir"] = colFileDir,
            ["tagtypes"] = colTagTypes,
            ["title"] = colTitle,
            ["artist"] = colArtist,
            ["album"] = colAlbum,
            ["albumartist"] = colAlbumArtist,
            ["year"] = colYear,
            ["trackstr"] = colTrackStr,
            ["discstr"] = colDiscStr,
            ["genre"] = colGenre,
            ["composer"] = colComposer,
            ["lyricist"] = colLyricist,
            ["comment"] = colComment,
            ["haspicture"] = colHasPicture,
            ["lyrics"] = colLyrics,
            ["channels"] = colChannels,
            ["samplerate"] = colSampleRate,
            ["bitrate"] = colBitRate,
            ["bitpersample"] = colBitPerSample,
            ["durationinms"] = colDurationInMs,
            ["updatetime"] = colUpdateTime,
        };
    }

    /// <summary>列拖拽重排后自动保存设置</summary>
    private void OnFileListViewColumnReordered(object? sender, ColumnReorderedEventArgs e)
    {
        // 在拖拽完成后保存
        BeginInvoke(() => SaveColumnSettings());
    }

    /// <summary>列宽调整后自动保存设置</summary>
    private void OnFileListViewColumnWidthChanged(object? sender, ColumnWidthChangedEventArgs e)
    {
        // 防抖：短暂延时后保存
        _columnWidthSaveTimer?.Stop();
        _columnWidthSaveTimer?.Start();
    }

    private System.Windows.Forms.Timer? _columnWidthSaveTimer;

    // ============================================================
    // 批量(B) — 歌词处理
    // ============================================================

    private void OnFormatLyricTimeline(object? sender, EventArgs e)
        => BatchLyricOp(l => _lyricService.ReformatTimetag(l), "格式化歌词时间轴完成");
    private void OnRemoveLyricTimeline(object? sender, EventArgs e)
        => BatchLyricOp(l => System.Text.RegularExpressions.Regex.Replace(l, @"\[\d{2}:\d{2}\.\d{2,3}\]", ""), "删除歌词时间轴完成");
    private void OnDeleteLyricBlankLines(object? sender, EventArgs e)
        => BatchLyricOp(l => string.Join("\n", l.Replace("\r\n", "\n").Replace("\r", "\n")
            .Split('\n').Where(line => !string.IsNullOrWhiteSpace(line))), "删除空白行完成");
    private void OnDeleteLyricHeadTag(object? sender, EventArgs e)
        => BatchLyricOp(l => string.Join("\n", l.Replace("\r\n", "\n").Replace("\r", "\n")
            .Split('\n').Where(line => !line.StartsWith("[ti:") && !line.StartsWith("[ar:") && !line.StartsWith("[al:") && !line.StartsWith("[by:") && !line.StartsWith("[offset:"))), "删除头部标签完成");

    private async void OnSaveLyricAsLrc(object? sender, EventArgs e)
    {
        var sel = GetCheckedOrSelectedFiles();
        if (sel.Count == 0) return;

        var savedPaths = new List<string>();
        var failedCount = 0;
        foreach (var f in sel.Where(f => !string.IsNullOrEmpty(f.Lyrics)))
        {
            var path = await _lyricService.SaveLrcFileAsync(f.Directory, f,
                new LyricInfo { OriginalLyric = f.Lyrics },
                new LyricInfo.SaveConfig
                {
                    SaveDirectory = _settings.SaveLrcDirectory ?? f.Directory,
                    FilenameFormat = _settings.SaveLrcFilenameFormat ?? "{artist} - {title}.lrc",
                    FileDefaultEncoding = _settings.SaveLrcFileDefaultEncoding ?? "utf-8"
                });
            if (path != null) savedPaths.Add(path);
            else failedCount++;
        }

        if (savedPaths.Count == 0 && failedCount == 0)
        {
            MessageBox.Show(this, "所选文件没有歌词可保存", "保存LRC",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var msg = $"另存完成（{savedPaths.Count} 个文件）：\n\n" + string.Join("\n", savedPaths);
        if (failedCount > 0) msg += $"\n\n{failedCount} 个文件保存失败";
        MessageBox.Show(this, msg, "保存LRC", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async void OnImportLrcToLyric(object? sender, EventArgs e)
    {
        var sel = GetCheckedOrSelectedFiles();
        if (sel.Count == 0) return;
        using var dlg = new OpenFileDialog { Title = "选择 LRC 文件", Filter = "LRC 歌词文件|*.lrc|文本文件|*.txt|所有文件|*.*", Multiselect = false };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var lrcText = File.ReadAllText(dlg.FileName);
        foreach (var f in sel) { f.Lyrics = lrcText; f.HasLyrics = true; f.IsModified = true; }
        if (_currentFile != null) tagEditPanel.UpdateLyrics(_currentFile.Lyrics ?? "");
        RefreshFileList();
    }

    private async void OnExtractCover(object? sender, EventArgs e)
    {
        var sel = GetCheckedOrSelectedFiles();
        if (sel.Count == 0) return;

        var savedPaths = new List<string>();
        var failedCount = 0;
        foreach (var f in sel.Where(f => f.HasCoverArt && f.CoverArtData is { Length: > 0 }))
        {
            var filename = $"{f.Artist} - {f.Title}".Trim(' ', '-');
            if (string.IsNullOrEmpty(filename)) filename = Path.GetFileNameWithoutExtension(f.FilePath);
            filename = Path.GetInvalidFileNameChars()
                .Aggregate(filename, (s, c) => s.Replace(c.ToString(), ""));
            var ext = f.CoverArtMimeType switch
            {
                "image/png" => ".png",
                "image/gif" => ".gif",
                "image/bmp" => ".bmp",
                "image/webp" => ".webp",
                _ => ".jpg"
            };

            var path = Path.Combine(f.Directory, filename + ext);
            try
            {
                await Task.Run(() => File.WriteAllBytes(path, f.CoverArtData!));
                savedPaths.Add(path);
            }
            catch
            {
                failedCount++;
            }
        }

        if (savedPaths.Count == 0 && failedCount == 0)
        {
            MessageBox.Show(this, "所选文件没有封面可提取", "提取封面",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var msg = $"提取完成（{savedPaths.Count} 个文件）：\n\n" + string.Join("\n", savedPaths);
        if (failedCount > 0) msg += $"\n\n{failedCount} 个文件提取失败";
        MessageBox.Show(this, msg, "提取封面", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnOpenCoverExternal(object? sender, EventArgs e)
    {
        if (_currentFile?.CoverArtData == null) return;
        try
        {
            var rel = _imageCache.StoreHistory(_currentFile.CoverArtData);
            if (string.IsNullOrEmpty(rel)) return;
            var full = _imageCache.GetHistoryFullPath(rel);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(full)
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    private async void OnCoverTypeChanged(object? sender, CoverPictureType type)
    {
        if (_currentFile == null || !_currentFile.HasCoverArt) return;
        _currentFile.CoverArtType = PicTypeToName(type);
        tagEditPanel.UpdateCoverInfo(_currentFile);
    }

    private void OnCoverIndexChanged(object? sender, EventArgs e)
    {
        if (_currentFile == null) return;
        _currentFile.CurrentPictureIndex = tagEditPanel.CurrentPictureIndex;

        var pics = tagEditPanel.Pictures;
        int idx = tagEditPanel.CurrentPictureIndex;
        if (pics != null && idx >= 0 && idx < pics.Count)
        {
            var pic = pics[idx];
            _currentFile.CoverArtData = pic.ImageData;
            _currentFile.CoverArtMimeType = pic.MimeType;
            _currentFile.HasCoverArt = true;
        }

        LoadFileToEditor(_currentFile);
    }

    private static string PicTypeToName(CoverPictureType type) => type switch
    {
        CoverPictureType.FrontCover => "封面",
        CoverPictureType.BackCover => "封底",
        CoverPictureType.LeafletPage => "插页",
        CoverPictureType.Media => "介质",
        CoverPictureType.LeadArtist => "主要艺术家",
        CoverPictureType.Artist => "艺术家",
        CoverPictureType.Conductor => "指挥",
        CoverPictureType.Band => "乐队",
        CoverPictureType.Composer => "作曲家",
        CoverPictureType.Lyricist => "作词家",
        CoverPictureType.RecordingLocation => "录制地点",
        CoverPictureType.DuringRecording => "录制中",
        CoverPictureType.DuringPerformance => "表演中",
        CoverPictureType.MovieScreenCapture => "电影截图",
        CoverPictureType.Illustration => "插图",
        CoverPictureType.BandLogo => "乐队标志",
        CoverPictureType.PublisherLogo => "出版商标志",
        CoverPictureType.FileIcon => "文件图标",
        CoverPictureType.OtherFileIcon => "其他文件图标",
        CoverPictureType.ColoredFish => "彩色鱼",
        CoverPictureType.NotAPicture => "非图片",
        _ => "其他"
    };

    // 文件(F) — 简繁转换（标签文本）
    private void OnTagChsToCht(object? sender, EventArgs e) => BatchTagConvert(true);
    private void OnTagChtToChs(object? sender, EventArgs e) => BatchTagConvert(false);

    // 批量(B) — 简繁转换
    private void OnBatchTagChtToChs(object? sender, EventArgs e) => BatchTagConvert(false);
    private void OnBatchTagChsToCht(object? sender, EventArgs e) => BatchTagConvert(true);
    private void OnBatchFilenameChsToCht(object? sender, EventArgs e) => BatchFilenameConvert(true);
    private void OnBatchFilenameChtToChs(object? sender, EventArgs e) => BatchFilenameConvert(false);

    /// <summary>菜单「编码修正」— 对第一个非空字段打开修正对话框</summary>
    private void OnEncodingFix(object? sender, EventArgs e)
    {
        if (_currentFile == null) return;

        // 收集所有非空字段 — 一次修复全部标签
        var fieldPairs = new[]
        {
            new KeyValuePair<string, string>("title", tagEditPanel.GetFieldText("title")),
            new KeyValuePair<string, string>("artist", tagEditPanel.GetFieldText("artist")),
            new KeyValuePair<string, string>("album", tagEditPanel.GetFieldText("album")),
            new KeyValuePair<string, string>("genre", tagEditPanel.GetFieldText("genre")),
            new KeyValuePair<string, string>("albumartist", tagEditPanel.GetFieldText("albumartist")),
            new KeyValuePair<string, string>("composer", tagEditPanel.GetFieldText("composer")),
            new KeyValuePair<string, string>("lyricist", tagEditPanel.GetFieldText("lyricist")),
            new KeyValuePair<string, string>("comment", tagEditPanel.GetFieldText("comment")),
            new KeyValuePair<string, string>("lyrics", tagEditPanel.GetFieldText("lyrics")),
        }.Where(f => !string.IsNullOrEmpty(f.Value)).ToArray();

        if (fieldPairs.Length == 0)
        {
            MessageBox.Show(this, "没有可修正的字段", "编码修正", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // 打开多字段编码修正对话框，展示所有字段的预览
        using var dlg = new EncodingFixForm((IEnumerable<KeyValuePair<string, string>>)fieldPairs);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.FixedFields != null)
        {
            // 将选中的编码应用到所有字段
            foreach (var kv in dlg.FixedFields)
            {
                tagEditPanel.SetFieldText(kv.Key, kv.Value);
            }
        }
    }

    private void OnOfficialSite(object? sender, EventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/lkjo1989/musictag-clone")
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    // ============================================================
    // 歌词编辑对话框 (TagEditPanel 事件)
    // ============================================================

    private void OnLyricsEdit(object? sender, EventArgs e)
    {
        if (_currentFile == null) return;
        using var dlg = new LyricsEditForm(_currentFile, _lyricService, _tagService, _settings);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        if (dlg.Saved)
        {
            // 确定并保存 — 已写盘，刷新歌词预览并清除"(已修改)"
            tagEditPanel.UpdateLyricsPreview(_currentFile.Lyrics ?? "");
            tagEditPanel.ResetFieldLabels();
        }
        else
        {
            // 确定 — 仅内存修改，标记"(已修改)"
            tagEditPanel.UpdateLyrics(_currentFile.Lyrics ?? "");
        }
    }

    /// <summary>编码修正按钮点击 — 打开编码修正对话框</summary>
    private void OnEncodingFixForField(object? sender, EncodingFixEventArgs e)
    {
        if (_currentFile == null) return;

        using var dlg = new EncodingFixForm(e.FieldName, e.Text);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.FixedText != null)
        {
            tagEditPanel.SetFieldText(e.FieldName, dlg.FixedText);
        }
    }

    private void OpenLyricsEdit()
    {
        if (_currentFile == null) { MessageBox.Show(this, "请先选择文件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        OnLyricsEdit(this, EventArgs.Empty);
    }

    /// <summary>批量歌词处理辅助</summary>
    private void BatchLyricOp(Func<string, string> transform, string doneMsg)
    {
        var sel = GetCheckedOrSelectedFiles();
        if (sel.Count == 0) return;
        var count = 0;
        foreach (var f in sel.Where(f => !string.IsNullOrEmpty(f.Lyrics)))
        { f.Lyrics = transform(f.Lyrics!); f.IsModified = true; count++; }
        if (_currentFile != null) tagEditPanel.UpdateLyrics(_currentFile.Lyrics ?? "");
        RefreshFileList();
        MessageBox.Show(this, $"{doneMsg} ({count} 个文件)", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>简繁转换：只改内存数据和左侧栏，不动文件列表</summary>
    private void BatchTagConvert(bool toTraditional)
    {
        var sel = GetCheckedOrSelectedFiles();
        if (sel.Count == 0) return;
        Func<string, string> conv = toTraditional ? ChineseConverter.SimplifiedToTraditional : ChineseConverter.TraditionalToSimplified;
        bool hasLyrics = false;
        foreach (var f in sel)
        {
            f.Title = conv(f.Title);
            f.Artist = conv(f.Artist);
            f.Album = conv(f.Album);
            f.Genre = conv(f.Genre);
            f.AlbumArtist = conv(f.AlbumArtist);
            f.Composer = conv(f.Composer);
            f.Lyricist = conv(f.Lyricist);
            f.Comment = conv(f.Comment);
            if (!string.IsNullOrEmpty(f.Lyrics))
            {
                f.Lyrics = conv(f.Lyrics);
                if (f == _currentFile) hasLyrics = true;
            }
            f.IsModified = true;
        }
        if (_currentFile != null)
        {
            tagEditPanel.UpdateFieldTexts(_currentFile);
            if (hasLyrics) tagEditPanel.UpdateLyrics(_currentFile.Lyrics ?? "");
        }
    }

    /// <summary>批量文件名简繁转换</summary>
    private void BatchFilenameConvert(bool toTraditional)
    {
        var sel = GetCheckedOrSelectedFiles();
        if (sel.Count == 0) return;
        Func<string, string> conv = toTraditional ? ChineseConverter.SimplifiedToTraditional : ChineseConverter.TraditionalToSimplified;
        var count = 0;
        foreach (var f in sel) { var nn = conv(Path.GetFileNameWithoutExtension(f.FilePath)); var np = Path.Combine(f.Directory, nn + f.Extension); if (np != f.FilePath) { File.Move(f.FilePath, np); f.FilePath = np; count++; } }
        RefreshFileList();
        MessageBox.Show(this, $"已转换 {count} 个文件名", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ============================================================
    // 自动匹配与批量操作
    // ============================================================

    private async void OnBatchAutoMatch(object? sender, EventArgs e)
    {
        var selected = GetCheckedOrSelectedFiles();
        if (selected.Count == 0) return;

        using var optionsForm = new AutoMatchTagsForm(_settings);
        if (optionsForm.ShowDialog(this) != DialogResult.OK) return;

        var overwriteReadOnly = false;
        if (optionsForm.Options.HasTagFields)
        {
            var readOnly = selected.FirstOrDefault(f => File.Exists(f.FilePath) &&
                (File.GetAttributes(f.FilePath) & FileAttributes.ReadOnly) != 0);
            if (readOnly != null)
            {
                var choice = MessageBox.Show(this,
                    $"文件“{readOnly.FileName}”等为只读。\n\n选择“是”将临时解除只读并写入；选择“否”将跳过只读文件。",
                    "发现只读文件", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (choice == DialogResult.Cancel) return;
                overwriteReadOnly = choice == DialogResult.Yes;
            }
        }

        if (optionsForm.Options.HasTagFields)
        {
            foreach (var file in selected)
                _tagHistory.TryAddHistory(file.FilePath, file);
        }

        using var cts = new CancellationTokenSource();
        using var progressForm = new AutoMatchProgressForm(selected.Count, cts);
        SetBusy(true, "批量自动匹配标签...");
        progressForm.Show(this);
        try
        {
            var progress = new Progress<int>(progressForm.SetProgress);
            var current = new Progress<string>(progressForm.SetFile);
            var result = await _autoMatch.ExecuteAsync(selected, optionsForm.Options,
                progress, current, overwriteReadOnly, cts.Token);

            if (!progressForm.IsDisposed) progressForm.Close();
            var reload = selected.Where(f => result.Files.Any(r =>
                string.Equals(r.FilePath, f.FilePath, StringComparison.OrdinalIgnoreCase) && r.Written)).ToList();
            if (reload.Count > 0) await ReloadSavedFilesTagsAsync(reload);

            var canceled = cts.IsCancellationRequested ? "，已取消" : string.Empty;
            MessageBox.Show(this,
                $"批量完成: 匹配 {result.MatchedCount}，写入 {result.WrittenCount}，跳过 {result.SkippedCount}，失败 {result.ErrorCount}{canceled}",
                "批量匹配", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "批量自动匹配失败");
            MessageBox.Show(this, "自动匹配失败：" + ex.Message, "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!progressForm.IsDisposed) progressForm.Close();
            SetBusy(false, "");
        }
    }

    private void OnBatchFilenameRel(object? sender, EventArgs e)
        => ShowFilenameRelation(null);

    private async void ShowFilenameRelation(FilenameRelationMode? forcedMode)
    {
        var selected = GetCheckedOrSelectedFiles();
        if (selected.Count == 0) return;

        using var dialog = new FilenameRelationForm(_settings, forcedMode);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var overwriteReadOnly = false;
        if (dialog.Options.Mode == FilenameRelationMode.ChangeTags)
        {
            var hasReadOnly = selected.Any(file =>
                (File.GetAttributes(file.FilePath) & FileAttributes.ReadOnly) != 0);
            if (hasReadOnly)
            {
                var answer = MessageBox.Show(this,
                    "选中的文件中包含只读文件。是否临时取消只读属性并继续？",
                    "文件名相关", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                overwriteReadOnly = answer == DialogResult.Yes;
            }
            foreach (var file in selected) _tagHistory.TryAddHistory(file.FilePath, file);
        }

        SetBusy(true, "正在处理文件名相关操作...");
        try
        {
            var progress = new Progress<int>(value =>
                statusLabel.Text = $"正在处理文件名相关操作... {value}/{selected.Count}");
            var result = await _filenameRelation.ExecuteAsync(selected, dialog.Options,
                overwriteReadOnly, progress);

            if (result.TagChangedFiles.Count > 0)
                await ReloadSavedFilesTagsAsync(result.TagChangedFiles);
            RefreshFileList();

            var detail = result.Errors.Count == 0
                ? string.Empty
                : "\n\n" + string.Join("\n", result.Errors.Take(5)) +
                  (result.Errors.Count > 5 ? "\n..." : string.Empty);
            MessageBox.Show(this,
                $"处理完成：成功 {result.ChangedCount}，跳过 {result.SkippedCount}，失败 {result.ErrorCount}{detail}",
                "文件名相关", MessageBoxButtons.OK,
                result.ErrorCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "文件名相关操作失败");
            MessageBox.Show(this, "操作失败：" + ex.Message, "文件名相关",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, string.Empty);
        }
    }

    // ============================================================
    // 工具栏新增操作
    // ============================================================

    private void OnRemoveFile(object? sender, EventArgs e)
    {
        try
        {
            var selected = GetSelectedFiles();
            if (selected.Count == 0) return;

            foreach (var f in selected)
                _files.Remove(f);
            tagEditPanel.LoadFromFile(new MusicFile()); // 清空编辑器
            RefreshFileList();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "OnRemoveFile 失败");
            MessageBox.Show(this, $"操作失败：{ex.Message}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnMoveUp(object? sender, EventArgs e)
    {
        MoveSelectedItem(-1);
    }

    private void OnMoveDown(object? sender, EventArgs e)
    {
        MoveSelectedItem(1);
    }

    private void MoveSelectedItem(int direction)
    {
        if (fileListView.SelectedIndices.Count != 1) return;

        var oldIndex = fileListView.SelectedIndices[0];
        var newIndex = oldIndex + direction;

        if (newIndex < 0 || newIndex >= _files.Count) return;

        var item = _files[oldIndex];
        _files.RemoveAt(oldIndex);
        _files.Insert(newIndex, item);

        RefreshFileList();
        fileListView.Items[newIndex].Selected = true;
        fileListView.EnsureVisible(newIndex);
    }

    // ============================================================
    // 菜单与工具栏
    // ============================================================

    private async void OnRefresh(object? sender, EventArgs e)
    {
        ClearSidebar();
        if (_files.Count > 0)
        {
            var dir = _files.First().Directory;
            if (Directory.Exists(dir))
                await ScanAndLoadFilesAsync(dir);
            else
                RefreshFileList();
        }
    }

    private void OnDiscardChanges(object? sender, EventArgs e)
    {
        if (_currentFile != null)
        {
            _ = LoadTagsFromFile(_currentFile);
        }
    }

    private void OnSelectAll(object? sender, EventArgs e)
    {
        fileListView.BeginUpdate();
        foreach (ListViewItem item in fileListView.Items)
            item.Selected = true;
        fileListView.EndUpdate();
        UpdateStatusBar();
    }

    private void OnDeselectAll(object? sender, EventArgs e)
    {
        try
        {
            fileListView.BeginUpdate();
            foreach (ListViewItem item in fileListView.Items)
                item.Selected = false;
            fileListView.EndUpdate();
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "OnDeselectAll 失败");
        }
    }

    private void OnSettings(object? sender, EventArgs e)
    {
        using var dlg = Program.Services.GetRequiredService<SettingsForm>();
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _settings.Save();
        }
    }

    private async void OnCheckUpdate(object? sender, EventArgs e)
    {
        MessageBox.Show(this, $"MusicTag Clone v{AppInfo.VersionString}\n已是最新版本。", "检查更新",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        await Task.CompletedTask;
    }

    private void OnAbout(object? sender, EventArgs e)
    {
        using var dlg = Program.Services.GetRequiredService<AboutDialog>();
        dlg.ShowDialog(this);
    }

    private void OnExit(object? sender, EventArgs e)
    {
        Application.Exit();
    }

    /// <summary>保存前记录当前标签状态到历史</summary>
    private void RecordTagHistoryBeforeSave()
    {
        if (_currentFile == null) return;
        // 标签已全部清空（原始值已在清除时入历史），空状态无需重复记录
        if (!HasAnyTagValue(_currentFile)) return;
        _tagHistory.TryAddHistory(_currentFile.FilePath, _currentFile);
    }

    /// <summary>判断文件当前是否有任何标签内容（决定是否需要记录历史）</summary>
    private static bool HasAnyTagValue(MusicFile f) =>
        !string.IsNullOrEmpty(f.Title) || !string.IsNullOrEmpty(f.Artist) ||
        !string.IsNullOrEmpty(f.Album) || (f.Year ?? 0) > 0 ||
        (f.Track ?? 0) > 0 || (f.Disc ?? 0) > 0 ||
        !string.IsNullOrEmpty(f.Genre) || !string.IsNullOrEmpty(f.AlbumArtist) ||
        !string.IsNullOrEmpty(f.Composer) || !string.IsNullOrEmpty(f.Lyricist) ||
        !string.IsNullOrEmpty(f.Comment) || !string.IsNullOrEmpty(f.Lyrics) ||
        f.HasCoverArt;

    /// <summary>文件 > 标签历史 — 显示当前文件的标签历史记录，允许选择恢复</summary>
    private void OnTagHistory(object? sender, EventArgs e)
    {
        if (_currentFile == null) return;

        var records = _tagHistory.GetHistory(_currentFile.FilePath);
        if (records.Count == 0)
        {
            MessageBox.Show(this, "当前文件暂无标签历史", "标签历史",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new TagHistoryForm(_tagHistory, _currentFile.FilePath, records);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedRecord != null)
        {
            var rec = dlg.SelectedRecord;

            // 应用选中的历史值到编辑器（全部文本字段）
            tagEditPanel.SetFieldText("title", rec.Title ?? "");
            tagEditPanel.SetFieldText("artist", rec.Artist ?? "");
            tagEditPanel.SetFieldText("album", rec.Album ?? "");
            tagEditPanel.SetFieldText("year", rec.Year ?? "");
            tagEditPanel.SetFieldText("track", rec.TrackStr ?? "");
            tagEditPanel.SetFieldText("disc", rec.DiscStr ?? "");
            tagEditPanel.SetFieldText("genre", rec.Genre ?? "");
            tagEditPanel.SetFieldText("albumartist", rec.AlbumArtist ?? "");
            tagEditPanel.SetFieldText("composer", rec.Composer ?? "");
            tagEditPanel.SetFieldText("lyricist", rec.Lyricist ?? "");
            tagEditPanel.SetFieldText("comment", rec.Comment ?? "");

            // 歌词
            if (!string.IsNullOrEmpty(rec.Lyrics))
                tagEditPanel.UpdateLyrics(rec.Lyrics);
            else
                tagEditPanel.UpdateLyrics("");

            // 封面
            if (dlg.SelectedCoverData != null && dlg.SelectedCoverData.Length > 0)
            {
                _currentFile.CoverArtData = dlg.SelectedCoverData;
                _currentFile.HasCoverArt = true;
                _currentFile.CoverArtMimeType = "image/jpeg";
                RemoveCoverCache(_currentFile.FilePath);
                LoadCoverImage(_currentFile);
            }

            // 同步更新 MusicFile 对象，标记已修改
            if (!string.IsNullOrEmpty(rec.Title)) _currentFile.Title = rec.Title;
            if (!string.IsNullOrEmpty(rec.Artist)) _currentFile.Artist = rec.Artist;
            if (!string.IsNullOrEmpty(rec.Album)) _currentFile.Album = rec.Album;
            if (int.TryParse(rec.Year, out var year)) _currentFile.Year = (uint)year;
            if (int.TryParse(rec.TrackStr, out var track)) _currentFile.Track = (uint)track;
            if (int.TryParse(rec.DiscStr, out var disc)) _currentFile.Disc = (uint)disc;
            if (!string.IsNullOrEmpty(rec.Genre)) _currentFile.Genre = rec.Genre;
            if (!string.IsNullOrEmpty(rec.AlbumArtist)) _currentFile.AlbumArtist = rec.AlbumArtist;
            if (!string.IsNullOrEmpty(rec.Composer)) _currentFile.Composer = rec.Composer;
            if (!string.IsNullOrEmpty(rec.Lyricist)) _currentFile.Lyricist = rec.Lyricist;
            if (!string.IsNullOrEmpty(rec.Comment)) _currentFile.Comment = rec.Comment;
            if (!string.IsNullOrEmpty(rec.Lyrics)) { _currentFile.Lyrics = rec.Lyrics; _currentFile.HasLyrics = true; }
            _currentFile.IsModified = true;

            // 刷新文件列表行
            fileListView.BeginUpdate();
            UpdateFileListItem(_currentFile);
            fileListView.EndUpdate();
        }
    }
    // ============================================================
    // 过滤与排序
    // ============================================================

    private void OnFilterTextChanged(object? sender, EventArgs e)
    {
        ApplyFilter(filterCombo.SelectedIndex > 0
            ? $".{filterCombo.SelectedItem!.ToString()!.ToLowerInvariant()}" : "");
    }

    private void ApplyFilter(string typeFilter)
    {
        var keyword = filterTextBox.Text;
        var visible = GetVisibleFiles();
        var filtered = _fileScanner.FilterFiles(visible, keyword, typeFilter);
        PopulateListView(filtered);
        UpdateStatusBar();
    }

    private void ApplySort()
    {
        var visible = GetVisibleFiles();
        var sorted = _fileScanner.SortFiles(visible, _sortField, _sortAscending);
        PopulateListView(sorted);
    }

    // ============================================================
    // 窗口管理
    // ============================================================

    private void OnMainFormClosing(object? sender, FormClosingEventArgs e)
    {
        _coverLoadCts?.Cancel();
        _coverLoadCts?.Dispose();
        _coverLoadCts = null;

        // 清理封面 Image 缓存
        foreach (var img in CoverCache.Values)
            img?.Dispose();
        CoverCache.Clear();
        CoverCacheOrder.Clear();

        // 保存列设置（同步当前列宽）
        SyncColumnWidthsFromListView();
        _settings.ListviewColumnHeader = JsonConvert.SerializeObject(_columnSettings);

        // 保存文件列表（下次启动恢复）
        SaveFileList();

        // 保存窗口状态
        _settings.Maximized = WindowState == FormWindowState.Maximized;
        if (WindowState == FormWindowState.Normal)
        {
            _settings.MainFormPosSizeInfo = $"{Left},{Top},{Width},{Height}";
        }

        // 持久化到 SQLite
        _settings.Save();

    }

    // ============================================================
    // 辅助方法
    // ============================================================

    private void RestoreWindowState()
    {
        if (!string.IsNullOrEmpty(_settings.MainFormPosSizeInfo))
        {
            var parts = _settings.MainFormPosSizeInfo.Split(',');
            if (parts.Length == 4 &&
                int.TryParse(parts[0], out var x) &&
                int.TryParse(parts[1], out var y) &&
                int.TryParse(parts[2], out var w) &&
                int.TryParse(parts[3], out var h))
            {
                StartPosition = FormStartPosition.Manual;
                Left = x;
                Top = y;
                Width = w;
                Height = h;
            }
        }

        if (_settings.Maximized)
            WindowState = FormWindowState.Maximized;
    }

    private void RefreshFileList()
    {
        ApplyFilter("");
        ApplySort();
        RefreshMenuStates();
    }

    /// <summary>根据当前文件选中状态刷新菜单置灰</summary>
    private void RefreshMenuStates()
    {
        var hasFile = _currentFile != null;
        var hasItems = fileListView.Items.Count > 0;
        var hasChecked = GetCheckedOrSelectedFiles().Count > 0;

        // 文件: items[4..9] 需文件选中
        for (int i = 4; i <= 9; i++)
            fileMenuItem.DropDownItems[i].Enabled = hasFile;
        // 编辑
        selectAllMenuItem.Enabled = hasItems;
        deselectAllMenuItem.Enabled = hasItems;
        invertSelectMenuItem.Enabled = hasItems;
        for (int i = 4; i <= 7; i++)
            editMenuItem.DropDownItems[i].Enabled = hasFile;
        // 标签源 / 批量
        sourceMenuItem.Enabled = hasFile;
        batchMenuItem.Enabled = hasChecked;
        // 工具栏图片/歌词/组合标签源下拉按钮：需文件选中
        picSourceBtn.Enabled = hasFile;
        lrcSourceBtn.Enabled = hasFile;
        combSourceBtn.Enabled = hasFile;
        // 工具栏批量组按钮：与菜单「批量」一致，需有选中文件
        autoMatchBtn.Enabled = hasChecked;
        saveLrcBtn.Enabled = hasChecked;
        extractCoverBtn.Enabled = hasChecked;
        filenameRelBtn.Enabled = hasChecked;
        // 工具栏第二组（标签相关）按钮：需文件选中
        saveTagsBtn.Enabled = hasFile;
        clearTagsBtn.Enabled = hasFile;
        undoBtn.Enabled = hasFile;
        readTagsBtn.Enabled = hasFile;
        encodingFixBtn.Enabled = hasFile;
        chsChtBtn.Enabled = hasFile;
        tagHistoryBtn.Enabled = hasFile;
    }

    /// <summary>获取当前可见的文件列表（排除隐藏目录中的文件）</summary>
    private List<MusicFile> GetVisibleFiles() =>
        _files.Where(f => !_hiddenDirs.Contains(f.Directory)).ToList();

    /// <summary>更新文件列表中对应行的标签值（在列索引已知的范围内更新）</summary>
    private void UpdateFileListItem(MusicFile file)
    {
        foreach (ListViewItem item in fileListView.Items)
        {
            if (item.Tag != file) continue;

            var dur = file.Duration.TotalSeconds > 0
                ? $"{(int)file.Duration.TotalMinutes}:{file.Duration.Seconds:D2}" : "";

            var lyricPreview = "";
            if (file.HasLyrics && !string.IsNullOrEmpty(file.Lyrics))
            {
                var line = file.Lyrics.Replace("\r\n", "\n").Split('\n')[0].Trim('\n', '\r');
                if (line.Length > 20) line = line.Substring(0, 20) + "…";
                lyricPreview = line;
            }

            // SubItem 索引: 0=文件名(text), 1=目录, 2=标签格式, 3=标题, 4=艺术家, 5=专辑
            // 6=专辑艺术家, 7=年份, 8=音轨, 9=碟号, 10=风格, 11=作曲家, 12=作词家
            // 13=注释, 14=封面, 15=歌词, 16=声道, 17=采样率, 18=比特率, 19=位深, 20=时长, 21=更新时间
            item.SubItems[3].Text = file.Title ?? "";
            item.SubItems[4].Text = file.Artist ?? "";
            item.SubItems[5].Text = file.Album ?? "";
            item.SubItems[6].Text = file.AlbumArtist ?? "";
            item.SubItems[7].Text = file.Year > 0 ? file.Year.ToString() : "";
            item.SubItems[8].Text = file.Track > 0 ? file.Track.ToString() : "";
            item.SubItems[9].Text = file.Disc > 0 ? file.Disc.ToString() : "";
            item.SubItems[10].Text = file.Genre ?? "";
            item.SubItems[11].Text = file.Composer ?? "";
            item.SubItems[12].Text = file.Lyricist ?? "";
            item.SubItems[13].Text = file.Comment ?? "";
            item.SubItems[14].Text = file.HasCoverArt ? "✓" : "";
            item.SubItems[15].Text = lyricPreview;
            item.SubItems[16].Text = file.Channels > 0 ? file.Channels.ToString() : "";
            item.SubItems[17].Text = file.SampleRate > 0 ? $"{file.SampleRate / 1000.0:F1}k" : "";
            item.SubItems[18].Text = file.BitRate > 0 ? $"{file.BitRate} kbps" : "";
            item.SubItems[19].Text = file.BitsPerSample > 0 ? $"{file.BitsPerSample} bit" : "";
            item.SubItems[20].Text = dur;
            item.SubItems[21].Text = file.LastModified.ToString("yyyy-MM-dd HH:mm");
            break;
        }
    }

    private void PopulateListView(IEnumerable<MusicFile> files)
    {
        fileListView.BeginUpdate();
        fileListView.Items.Clear();
        foreach (var f in files)
        {
            // 时长 mm:ss
            var dur = f.Duration.TotalSeconds > 0
                ? $"{(int)f.Duration.TotalMinutes}:{f.Duration.Seconds:D2}" : "";

            // 歌词第一行
            var lyricPreview = "";
            if (f.HasLyrics && !string.IsNullOrEmpty(f.Lyrics))
            {
                var line = f.Lyrics.Replace("\r\n", "\n").Split('\n')[0].Trim('\n', '\r');
                if (line.Length > 20) line = line.Substring(0, 20) + "…";
                lyricPreview = line;
            }

            var item = new ListViewItem
            {
                Text = f.FileName,
                Tag = f
            };
            item.SubItems.AddRange(new[]
            {
                f.Directory,                   // 目录
                f.TagFormat ?? f.AudioFormat,  // 标签格式
                f.Title,                       // 标题
                f.Artist,                      // 艺术家
                f.Album,                       // 专辑
                f.AlbumArtist,                 // 专辑艺术家
                f.Year > 0 ? f.Year.ToString() : "", // 年份
                f.Track > 0 ? f.Track.ToString() : "", // 音轨
                f.Disc > 0 ? f.Disc.ToString() : "",   // 碟号
                f.Genre,                       // 风格
                f.Composer,                    // 作曲家
                f.Lyricist,                    // 作词家
                f.Comment,                     // 注释
                f.HasCoverArt ? "✓" : "",      // 封面
                lyricPreview,                    // 歌词（第一行）
                f.Channels > 0 ? f.Channels.ToString() : "",  // 声道
                f.SampleRate > 0 ? $"{f.SampleRate / 1000.0:F1}k" : "",  // 采样率
                f.BitRate > 0 ? $"{f.BitRate} kbps" : "",      // 比特率
                f.BitsPerSample > 0 ? $"{f.BitsPerSample} bit" : "",  // 位深
                dur,                           // 时长
                f.LastModified.ToString("yyyy-MM-dd HH:mm"),   // 更新时间
            });
            fileListView.Items.Add(item);
        }
        fileListView.EndUpdate();
        // 强制刷新横向滚动条（刚加载时列宽超出但滚动条不出现）
        if (fileListView.Columns.Count > 0)
        {
            var last = fileListView.Columns[fileListView.Columns.Count - 1];
            last.Width -= 1;
            last.Width += 1;
        }
        UpdateStatusBar();
    }

    /// <summary>为一批 MusicFile 异步读取标签信息</summary>
    private async Task EnrichFilesAsync(IEnumerable<MusicFile> files)
    {
        SetBusy(true, "正在读取标签...");
        try
        {
            var list = files.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                var tags = await _tagService.ReadTagsAsync(list[i].FilePath);
                if (tags.Title != null) list[i].Title = tags.Title;
                if (tags.Artist != null) list[i].Artist = tags.Artist;
                if (tags.Album != null) list[i].Album = tags.Album;
                if (tags.Year.HasValue) list[i].Year = tags.Year.Value;
                if (tags.Track.HasValue) list[i].Track = tags.Track.Value;
                if (tags.TrackCount.HasValue) list[i].TrackCount = tags.TrackCount.Value;
                if (tags.Disc.HasValue) list[i].Disc = tags.Disc.Value;
                if (tags.DiscCount.HasValue) list[i].DiscCount = tags.DiscCount.Value;
                if (tags.Genre != null) list[i].Genre = tags.Genre;
                if (tags.AlbumArtist != null) list[i].AlbumArtist = tags.AlbumArtist;
                if (tags.Composer != null) list[i].Composer = tags.Composer;
                if (tags.Lyricist != null) list[i].Lyricist = tags.Lyricist;
                if (tags.Comment != null) list[i].Comment = tags.Comment;
                if (tags.Lyrics != null) { list[i].Lyrics = tags.Lyrics; list[i].HasLyrics = true; }
                if (tags.CoverArtData != null && tags.CoverArtData.Length > 0)
                {
                    list[i].HasCoverArt = true;
                    list[i].CoverArtMimeType = tags.CoverArtMimeType ?? "image/jpeg";
                    list[i].CoverArtData = tags.CoverArtData;
                    list[i].CoverArtType = tags.CoverArtType;
                    list[i].AllPictures = tags.AllPictures;
                }
                // 音频属性
                if (tags.DurationMs.HasValue)
                    list[i].Duration = TimeSpan.FromMilliseconds(tags.DurationMs.Value);
                if (tags.BitRate.HasValue) list[i].BitRate = tags.BitRate.Value;
                if (tags.SampleRate.HasValue) list[i].SampleRate = tags.SampleRate.Value;
                if (tags.Channels.HasValue) list[i].Channels = tags.Channels.Value;
                if (tags.BitsPerSample.HasValue) list[i].BitsPerSample = tags.BitsPerSample.Value;
                if (tags.TagFormat != null) list[i].TagFormat = tags.TagFormat;
            }
        }
        finally
        {
            SetBusy(false, "");
        }
        RefreshFileList();
    }

    private void UpdateStatusBar()
    {
        var totalCount = _files.Count;
        var totalDuration = TimeSpan.Zero;
        long totalSize = 0;

        foreach (var f in _files)
        {
            totalDuration += f.Duration;
            totalSize += f.FileSize;
        }

        var durationStr = totalDuration.TotalHours >= 1
            ? $"{totalDuration.Hours:D2}:{totalDuration.Minutes:D2}:{totalDuration.Seconds:D2}"
            : $"{totalDuration.Minutes:D2}:{totalDuration.Seconds:D2}";

        var sizeStr = totalSize switch
        {
            >= 1_073_741_824 => $"{totalSize / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{totalSize / 1_048_576.0:F1} MB",
            >= 1_024 => $"{totalSize / 1_024.0:F0} KB",
            _ => $"{totalSize} Byte"
        };

        infoLabel.Text = $"{fileListView.Items.Count} ({durationStr} | {sizeStr})";
    }

    private List<MusicFile> GetSelectedFiles()
    {
        return fileListView.SelectedItems.Cast<ListViewItem>()
            .Select(i => i.Tag as MusicFile)
            .Where(f => f != null && !_hiddenDirs.Contains(f.Directory))
            .ToList()!;
    }

    private List<MusicFile> GetCheckedOrSelectedFiles()
    {
        // 没有勾选框, 全部使用选中项
        return GetSelectedFiles();
    }

    private async Task LoadTagsFromFile(MusicFile file)
    {
        try
        {
            // 如果文件已富化过标签数据（含封面），直接加载编辑器，不再重新读取磁盘
            if (file.CoverArtData != null)
            {
                LoadFileToEditor(file);
                return;
            }

            _logger.Info("读取标签: {0}", file.FilePath);
            var tags = await _tagService.ReadTagsAsync(file.FilePath);
            // 防止异步竞争: 如果用户已切换到其他文件, 丢弃本结果
            if (_currentFile != file) return;

            if (tags.Title != null) file.Title = tags.Title;
            if (tags.Artist != null) file.Artist = tags.Artist;
            if (tags.Album != null) file.Album = tags.Album;
            if (tags.Year.HasValue) file.Year = tags.Year.Value;
            if (tags.Track.HasValue) file.Track = tags.Track.Value;
            if (tags.TrackCount.HasValue) file.TrackCount = tags.TrackCount.Value;
            if (tags.Disc.HasValue) file.Disc = tags.Disc.Value;
            if (tags.DiscCount.HasValue) file.DiscCount = tags.DiscCount.Value;
            if (tags.Genre != null) file.Genre = tags.Genre;
            if (tags.Comment != null) file.Comment = tags.Comment;
            if (tags.AlbumArtist != null) file.AlbumArtist = tags.AlbumArtist;
            if (tags.Composer != null) file.Composer = tags.Composer;
            if (tags.Lyricist != null) file.Lyricist = tags.Lyricist;
            if (tags.Lyrics != null) { file.Lyrics = tags.Lyrics; file.HasLyrics = true; }
            if (tags.CoverArtData != null && tags.CoverArtData.Length > 0)
            {
                file.HasCoverArt = true;
                file.CoverArtMimeType = tags.CoverArtMimeType ?? "image/jpeg";
                file.CoverArtData = tags.CoverArtData;
                file.CoverArtType = tags.CoverArtType;
                file.AllPictures = tags.AllPictures;
            }
            LoadFileToEditor(file);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "LoadTagsFromFile 失败: {0}", file.FilePath);
        }
    }

    private void SetBusy(bool busy, string statusText)
    {
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        progressBar.Visible = busy;
        if (busy)
        {
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            statusLabel.Text = statusText;
        }
        else
        {
            progressBar.Value = 0;
            // 非忙状态时清空中间状态文字，右下角 infoLabel 由 UpdateStatusBar 维护
            statusLabel.Text = "";
        }
        menuStrip.Enabled = !busy;
        toolStrip.Enabled = !busy;
    }

    /// <summary>清空左侧栏：置空 _currentFile、清除标签编辑器所有字段、封面图片等</summary>
    private void ClearSidebar()
    {
        _currentFile = null;
        tagEditPanel.LoadFromFile(new MusicFile());
        _coverLoadCts?.Cancel();
        _coverLoadCts?.Dispose();
        _coverLoadCts = null;
        SetCoverImage(null);
        RefreshMenuStates();
        UpdateStatusBar();
    }

    // ============================================================
    // 文件列表持久化 — 退出时保存，启动时恢复
    // ============================================================

    private void SaveFileList()
    {
        try
        {
            var paths = _files.Select(f => f.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _settings.ListViewFileSetting = JsonConvert.SerializeObject(paths);
        }
        catch { /* 保存失败不影响退出 */ }
    }

    private void RestoreFileList()
    {
        try
        {
            var json = _settings.ListViewFileSetting;
            if (string.IsNullOrEmpty(json)) return;

            var paths = JsonConvert.DeserializeObject<List<string>>(json);
            if (paths == null || paths.Count == 0) return;

            var newFiles = new List<MusicFile>();
            foreach (var path in paths)
            {
                if (File.Exists(path) && _fileScanner.IsSupportedFile(path))
                    newFiles.Add(MusicFile.FromPath(path));
            }

            if (newFiles.Count == 0) return;

            _files = newFiles;
            RefreshFileList();
            _ = EnrichFilesAsync(newFiles);
            statusLabel.Text = $"已恢复 {_files.Count} 个文件";
        }
        catch { /* 恢复失败不影响启动 */ }
    }
}
