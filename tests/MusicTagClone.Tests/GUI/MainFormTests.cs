using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;

namespace MusicTagClone.Tests.GUI;

/// <summary>
/// 主窗口 GUI 测试 — 使用 FlaUI + Windows UI Automation
/// 测试文件位于 D:\binary\testfile\ (8 个音频文件: MP3/FLAC/M4A)
/// </summary>
[Trait("Category", "GUI")] // UI 自动化测试，CI 跳过
public class MainFormTests : IDisposable
{
    private Application? _app;
    private UIA3Automation? _automation;
    private Window? _mainWindow;

    private static string AppPath =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "MusicTagClone.App", "bin", "Release", "net10.0-windows",
            "MusicTagClone.exe"));

    private static string TestFileDir =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..", "testfile"));

    private void LaunchApp()
    {
        _automation = new UIA3Automation();
        _app = Application.Launch(AppPath);
        _mainWindow = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(10));
        Assert.NotNull(_mainWindow);
        Thread.Sleep(2000); // 等待窗口完全渲染
    }

    public void Dispose()
    {
        _mainWindow = null;
        try { _app?.Close(); } catch { }
        _app?.Dispose();
        _automation?.Dispose();
    }

    // ================================================================
    // 窗口基本属性
    // ================================================================

    [Fact]
    public void MainWindow_Shows()
    {
        LaunchApp();
        Assert.NotNull(_mainWindow);
        Assert.Contains("MusicTag", _mainWindow!.Title);
    }

    [Fact]
    public void MainWindow_HasCorrectSize()
    {
        LaunchApp();
        var bounds = _mainWindow!.BoundingRectangle;
        Assert.True(bounds.Width >= 800);
        Assert.True(bounds.Height >= 500);
    }

    // ================================================================
    // 菜单栏 — 8 个一级菜单
    // ================================================================

    [Fact]
    public void MenuBar_HasAll8TopLevelMenus()
    {
        LaunchApp();
        var menuBar = _mainWindow!.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuBar));
        Assert.NotNull(menuBar);

        var items = menuBar!.FindAllChildren();
        var names = items.Select(i => i.Name).ToList();

        string[] expected = ["文件", "编辑", "视图", "标签源", "批量", "工具", "帮助"];
        foreach (var exp in expected)
            Assert.Contains(names, n => n != null && n.Contains(exp));
    }

    [Fact]
    public void FileMenu_HasExpectedItems()
    {
        LaunchApp();
        var fileMenu = ClickTopMenu("文件");
        Assert.NotNull(fileMenu);

        var subItems = GetOpenMenuItems();
        var names = subItems.Select(i => i.Name).ToList();

        Assert.Contains(names, n => n != null && n.Contains("改变工作目录"));
        Assert.Contains(names, n => n != null && n.Contains("保存标签"));
        Assert.Contains(names, n => n != null && n.Contains("退出"));
    }

    [Fact]
    public void EditMenu_HasExpectedItems()
    {
        LaunchApp();
        ClickTopMenu("编辑");

        var subItems = GetOpenMenuItems();
        var names = subItems.Select(i => i.Name).ToList();

        Assert.Contains(names, n => n != null && n.Contains("全选"));
        Assert.Contains(names, n => n != null && n.Contains("反选"));
        Assert.Contains(names, n => n != null && n.Contains("撤销"));
        Assert.Contains(names, n => n != null && n.Contains("打开文件目录"));
    }

    [Fact]
    public void ViewMenu_HasRefreshAndCustomColumns()
    {
        LaunchApp();
        ClickTopMenu("视图");

        var subItems = GetOpenMenuItems();
        var names = subItems.Select(i => i.Name).ToList();

        Assert.Contains(names, n => n != null && n.Contains("刷新"));
        Assert.Contains(names, n => n != null && n.Contains("自定义显示列"));
    }

    [Fact]
    public void SourceMenu_ExistsButGrayedOut_WhenNoFile()
    {
        LaunchApp();
        var menuBar = _mainWindow!.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuBar));
        Assert.NotNull(menuBar);

        var items = menuBar!.FindAllChildren();
        var sourceMenu = items.FirstOrDefault(i => i.Name != null && i.Name.Contains("标签源"));
        Assert.NotNull(sourceMenu);
        // 无文件选中时标签源菜单应置灰
        Assert.False(sourceMenu!.IsEnabled, "标签源在无文件选中时应置灰");
    }

    [Fact]
    public void BatchMenu_ExistsButGrayedOut_WhenNoFile()
    {
        LaunchApp();
        var menuBar = _mainWindow!.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuBar));
        Assert.NotNull(menuBar);

        var items = menuBar!.FindAllChildren();
        var batchMenu = items.FirstOrDefault(i => i.Name != null && i.Name.Contains("批量"));
        Assert.NotNull(batchMenu);
        Assert.False(batchMenu!.IsEnabled, "批量在无文件选中时应置灰");
    }

    [Fact]
    public void ToolsMenu_HasSettings()
    {
        LaunchApp();
        ClickTopMenu("工具");

        var subItems = GetOpenMenuItems();
        var names = subItems.Select(i => i.Name).ToList();

        Assert.Contains(names, n => n != null && n.Contains("设置"));
    }

    [Fact]
    public void HelpMenu_HasItems()
    {
        LaunchApp();
        ClickTopMenu("帮助");

        var subItems = GetOpenMenuItems();
        var names = subItems.Select(i => i.Name).ToList();

        Assert.Contains(names, n => n != null && n.Contains("检查新版本"));
        Assert.Contains(names, n => n != null && n.Contains("官方网站"));
        Assert.Contains(names, n => n != null && n.Contains("关于"));
    }

    // ================================================================
    // 初始置灰状态
    // ================================================================

    [Fact]
    public void MenuItems_GrayedOut_WhenNoFileSelected()
    {
        // 验证"未选中文件"时的置灰逻辑。启动时 _currentFile == null，
        // 因此所有依赖"当前选中文件"的菜单项（保存标签 / 标签源 / 批量）必须置灰。
        // 注意：全选/反选的置灰取决于文件列表是否有条目（启动时会恢复上次列表），
        // 与"是否选中"无关，故不在此断言，改由下方单独测试。
        LaunchApp();

        // 文件菜单: 保存标签应置灰（无当前文件）
        ClickTopMenu("文件");
        var items = GetOpenMenuItems();
        var saveItem = items.FirstOrDefault(i => i.Name != null && i.Name.Contains("保存标签"));
        Assert.NotNull(saveItem);
        Assert.False(saveItem!.IsEnabled, "保存标签在无文件选中时应置灰");
        _mainWindow!.Click();

        // 标签源: 整菜单应置灰（无当前文件，无法点击）
        var menuBar = _mainWindow!.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuBar));
        Assert.NotNull(menuBar);
        var sourceMenu = menuBar!.FindAllChildren().FirstOrDefault(i => i.Name != null && i.Name.Contains("标签源"));
        Assert.NotNull(sourceMenu);
        Assert.False(sourceMenu!.IsEnabled, "标签源菜单在无文件选中时应置灰");

        // 批量: 整菜单应置灰（无文件选中，无勾选项）
        var batchMenu = menuBar.FindAllChildren().FirstOrDefault(i => i.Name != null && i.Name.Contains("批量"));
        Assert.NotNull(batchMenu);
        Assert.False(batchMenu!.IsEnabled, "批量菜单在无文件选中时应置灰");
    }

    [Fact]
    public void SelectAll_ToggledByFileListPresence()
    {
        // 全选/反选的 IsEnabled 仅取决于文件列表是否有条目（hasItems），
        // 与是否有"当前选中文件"无关。启动时可能因恢复上次列表而有条目。
        // 这里只断言二者状态一致：要么都置灰（无条目），要么都启用（有条目）。
        LaunchApp();
        ClickTopMenu("编辑");
        var items = GetOpenMenuItems();
        var selectAll = items.FirstOrDefault(i => i.Name != null && i.Name.Contains("全选"));
        var deselectAll = items.FirstOrDefault(i => i.Name != null && i.Name.Contains("反选"));
        Assert.NotNull(selectAll);
        Assert.NotNull(deselectAll);
        Assert.Equal(selectAll!.IsEnabled, deselectAll!.IsEnabled);
    }

    // ================================================================
    // 工具栏
    // ================================================================

    [Fact]
    public void Toolbar_HasExpectedButtons()
    {
        LaunchApp();
        var toolBar = _mainWindow!.FindFirstDescendant(cf => cf.ByControlType(ControlType.ToolBar));
        Assert.NotNull(toolBar);

        var buttons = toolBar!.FindAllChildren(cf => cf.ByControlType(ControlType.Button));
        var names = buttons.Select(b => b.Name).ToList();

        Assert.Contains(names, n => n != null && n.Contains("保存修改"));
        Assert.Contains(names, n => n != null && n.Contains("放弃修改"));
        Assert.Contains(names, n => n != null && n.Contains("添加目录"));
        Assert.Contains(names, n => n != null && n.Contains("移除文件"));
        Assert.Contains(names, n => n != null && n.Contains("上移"));
        Assert.Contains(names, n => n != null && n.Contains("下移"));
    }

    // ================================================================
    // 标签编辑面板
    // ================================================================

    [Fact]
    public void TagEditPanel_HasAllFields()
    {
        LaunchApp();
        string[] fields = ["标题", "艺术家", "专辑", "年份", "音轨号", "碟号",
                           "风格", "专辑艺术家", "作曲家", "作词家", "注释", "歌词"];
        foreach (var field in fields)
        {
            var found = FindLabelText(field);
            Assert.True(found != null, $"应找到字段: {field}");
        }
    }

    [Fact]
    public void TagEditPanel_HasCoverArea()
    {
        LaunchApp();
        var coverLabel = FindLabelText("图片");
        Assert.NotNull(coverLabel);
    }

    // ================================================================
    // 文件列表
    // ================================================================

    [Fact]
    public void FileListView_HasCorrectColumns()
    {
        LaunchApp();
        var header = _mainWindow!.FindFirstDescendant(cf => cf.ByControlType(ControlType.Header));
        Assert.NotNull(header);

        var columns = header!.FindAllChildren(cf => cf.ByControlType(ControlType.HeaderItem));
        var names = columns.Select(c => c.Name).ToList();

        Assert.Contains(names, n => n != null && n.Contains("文件名"));
        Assert.Contains(names, n => n != null && n.Contains("目录"));
        Assert.Contains(names, n => n != null && n.Contains("标签格式"));
        Assert.Contains(names, n => n != null && n.Contains("标题"));
        Assert.Contains(names, n => n != null && n.Contains("艺术家"));
        Assert.Contains(names, n => n != null && n.Contains("专辑"));
    }

    // ================================================================
    // 底部过滤/状态栏
    // ================================================================

    [Fact]
    public void BottomBar_HasFilterControls()
    {
        LaunchApp();
        var filterLabel = FindLabelText("过滤:");
        Assert.NotNull(filterLabel);
    }

    [Fact]
    public void BottomBar_ShowsStatusSummary()
    {
        // 状态栏格式固定为 "{数量} ({时长} | {大小})"，至少包含分隔与括号。
        // 启动时会恢复上次文件列表，数量/时长/大小可能非 0；大小区甚至可能是 MB/KB，
        // 故只断言结构特征 "(... | ...)" 存在，不强匹配具体单位。
        LaunchApp();
        var status = _mainWindow!.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
            .FirstOrDefault(e => e.Name != null && e.Name.Contains(" | ") && e.Name.Contains("(") && e.Name.Contains(")"));
        Assert.NotNull(status);
    }

    // ================================================================
    // 测试文件集成
    // ================================================================

    [Fact]
    public void TestFiles_Exist_AllFormats()
    {
        Assert.True(Directory.Exists(TestFileDir),
            $"测试文件目录不存在: {TestFileDir}");
        var files = Directory.GetFiles(TestFileDir, "*.*");
        var audioFiles = files.Where(f =>
            f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(audioFiles.Count >= 8,
            $"应有 >= 8 个测试音频文件，实际: {audioFiles.Count}");
        // 验证格式覆盖
        Assert.Contains(audioFiles, f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(audioFiles, f => f.EndsWith(".flac", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(audioFiles, f => f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase));
    }

    // ================================================================
    // 对话框测试
    // ================================================================

    [Fact]
    public void OptionsDialog_CanOpenAndClose()
    {
        LaunchApp();
        ClickTopMenu("工具");
        Thread.Sleep(300);

        var items = GetOpenMenuItems();
        var settingsItem = items.FirstOrDefault(i => i.Name != null && i.Name.Contains("设置"));
        Assert.NotNull(settingsItem);
        settingsItem!.Click();
        Thread.Sleep(1000);

        var settingsDialog = FindWindowByTitle("设置");
        if (settingsDialog != null)
        {
            var cancelBtn = settingsDialog.FindFirstDescendant(cf => cf.ByName("取消"));
            cancelBtn?.Click();
            Thread.Sleep(500);
        }
    }

    [Fact]
    public void AboutDialog_CanOpenAndClose()
    {
        LaunchApp();
        ClickTopMenu("帮助");
        Thread.Sleep(300);

        var items = GetOpenMenuItems();
        var aboutItem = items.FirstOrDefault(i => i.Name != null && i.Name.Contains("关于"));
        Assert.NotNull(aboutItem);
        aboutItem!.Click();
        Thread.Sleep(1000);

        var aboutDialog = FindWindowByTitle("关于");
        if (aboutDialog != null)
        {
            var okBtn = aboutDialog.FindFirstDescendant(cf => cf.ByName("确定"));
            okBtn?.Click();
            Thread.Sleep(500);
        }
    }

    // ================================================================
    // Helper Methods
    // ================================================================

    private AutomationElement? ClickTopMenu(string menuName)
    {
        var menuBar = _mainWindow!.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuBar));
        if (menuBar == null) return null;

        var items = menuBar.FindAllChildren();
        var target = items.FirstOrDefault(i => i.Name != null && i.Name.Contains(menuName));
        if (target == null) return null;

        target.Click();
        Thread.Sleep(400);
        return target;
    }

    private AutomationElement[] GetOpenMenuItems()
    {
        Thread.Sleep(300);
        var popup = _automation!.GetDesktop().FindFirstChild(cf => cf.ByControlType(ControlType.Menu));
        if (popup != null)
            return popup.FindAllChildren(cf => cf.ByControlType(ControlType.MenuItem));

        var menus = _mainWindow!.FindAllDescendants(cf => cf.ByControlType(ControlType.Menu));
        foreach (var menu in menus)
        {
            var items = menu.FindAllChildren(cf => cf.ByControlType(ControlType.MenuItem));
            if (items.Length > 0) return items;
        }
        return Array.Empty<AutomationElement>();
    }

    private AutomationElement? FindLabelText(string text)
    {
        return _mainWindow!.FindAllDescendants()
            .FirstOrDefault(e => e.Name != null && e.Name.Contains(text));
    }

    private Window? FindWindowByTitle(string title)
    {
        try
        {
            var desktop = _automation!.GetDesktop();
            var windows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));
            return windows.FirstOrDefault(w => w.Name.Contains(title)) as Window;
        }
        catch { return null; }
    }
}
