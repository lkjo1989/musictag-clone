using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MusicTagClone.Forms;
using MusicTagClone.Win32;
using MusicTagClone.Interfaces;
using MusicTagClone.Services;

namespace MusicTagClone;

static class Program
{
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// 用于 WM_COPYDATA 的唯一标识，确认消息来自本程序自身。
    /// </summary>
    private static readonly IntPtr CopyDataId = new(0x4D546167); // "MTag"

    [STAThread]
    static void Main(string[] args)
    {
        // 检查是否已有运行中的实例
        if (TryBringExistingInstanceToFront(args))
            return;

        // 注册代码页编码提供程序（确保所有代码页在 .NET 6+ 上可用）
#if NET6_0_OR_GREATER
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif

#if NET6_0_OR_GREATER
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
#else
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
#endif

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        var logger = Services.GetRequiredService<ILoggerService>();

        // 全局未处理异常捕获
        Application.ThreadException += (s, e) =>
        {
            logger.Error(e.Exception, "UI 线程未处理异常");
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            if (ex != null)
                logger.Error(ex, "非 UI 线程未处理异常");
            else
                logger.Error($"非 UI 线程未处理异常 (非 Exception 类型): {e.ExceptionObject}");
        };

        var settings = Services.GetRequiredService<ISettingsService>();
        settings.Load();

        // 接线：ImageCacheService 清理历史孤儿时需要历史记录的引用集合（避免循环依赖）
        var imageCache = Services.GetRequiredService<IImageCache>();
        if (imageCache is ImageCacheService impl)
        {
            var history = Services.GetRequiredService<ITagHistoryService>();
            impl.SetReferencedCoverPathsProvider(() => history.GetAllReferencedCoverPaths());
        }

        // 启动时清理一次 URL 下载缓存（仅 cache\img\，不触碰 cache\history\）
        try { imageCache.Sweep(); }
        catch (Exception ex) { logger.Error(ex, "ImageCache Sweep 失败"); }

        Application.Run(Services.GetRequiredService<MainForm>());
    }

    /// <summary>
    /// 尝试找到已运行的主窗口并将其置于前台。
    /// 如果找到则返回 true，调用方应退出。
    /// </summary>
    private static bool TryBringExistingInstanceToFront(string[] args)
    {
        var currentProcess = Process.GetCurrentProcess();
        var currentLocation = currentProcess.MainModule?.FileName?.Replace("/", "\\")
            ?? string.Empty;

        foreach (var process in Process.GetProcessesByName(currentProcess.ProcessName))
        {
            if (process.Id == currentProcess.Id)
                continue;

            // 验证是可执行文件路径一致（防止同名进程误判）
            string? processPath = null;
            try { processPath = process.MainModule?.FileName; }
            catch { /* 权限不足等异常，跳过 */ }
            if (processPath == null || processPath.Replace("/", "\\") != currentLocation)
                continue;

            // 找到属于该进程的顶层窗口
            var hWnd = FindMainWindowForProcess(process);
            if (hWnd == IntPtr.Zero)
                continue;

            // 恢复窗口（如果最小化）
            NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(hWnd);

            // 传递命令行参数给已有实例
            if (args.Length > 0)
            {
                SendCommandLineArgs(hWnd, args);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// 为指定进程寻找主窗口句柄。
    /// 优先用 Process.MainWindowHandle；如果无效则用 FindWindowEx 枚举。
    /// </summary>
    private static IntPtr FindMainWindowForProcess(Process process)
    {
        // 优先尝试 MainWindowHandle
        var hWnd = process.MainWindowHandle;
        if (hWnd != IntPtr.Zero)
        {
            // 验证窗口标题是否为 MusicTag
            var sb = new StringBuilder(256);
            NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (title.StartsWith("MusicTag") || title.StartsWith("音乐标签") || title.StartsWith("音樂標籤"))
                return hWnd;
        }

        // MainWindowHandle 无效，用 FindWindowEx 枚举所有顶层子窗口
        var found = IntPtr.Zero;
        while (true)
        {
            found = NativeMethods.FindWindowEx(IntPtr.Zero, found, null, null);
            if (found == IntPtr.Zero)
                break;

            NativeMethods.GetWindowThreadProcessId(found, out var pid);
            if (pid != process.Id)
                continue;

            var sb = new StringBuilder(256);
            NativeMethods.GetWindowText(found, sb, sb.Capacity);
            var title = sb.ToString();
            if (title.StartsWith("MusicTag") || title.StartsWith("音乐标签") || title.StartsWith("音樂標籤"))
                return found;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// 通过 WM_COPYDATA 将命令行参数发送给已有实例。
    /// </summary>
    private static void SendCommandLineArgs(IntPtr hWnd, string[] args)
    {
        var data = string.Join("\n", args);
        var cds = new NativeMethods.COPYDATASTRUCT
        {
            dwData = CopyDataId,
            cbData = (data.Length + 1) * 2, // Unicode 字节数
            lpData = data,
        };
        NativeMethods.SendMessage(hWnd, NativeMethods.WM_COPYDATA, IntPtr.Zero, ref cds);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // 先注册设置服务，以便后续读取代理配置
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILoggerService, LoggerService>();

        // 注册 HttpClient（直连，不含代理；代理由各服务按源配置）
        services.AddHttpClient("default")
            .ConfigurePrimaryHttpMessageHandler(_ =>
            {
                return new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                };
            });

        // 注册默认 HttpClient（直连；各服务通过 IHttpClientFactory 按源创建代理客户端）
        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return factory.CreateClient("default");
        });

        services.AddSingleton<ITagService, TagService>();
        services.AddSingleton<ILyricService, LyricService>();
        services.AddSingleton<IImageCache, ImageCacheService>();
        services.AddSingleton<ICoverService, CoverService>();
        services.AddSingleton<IFileScannerService, FileScannerService>();
        services.AddSingleton<WebSearchService>();
        services.AddSingleton<AutoMatchService>();
        services.AddSingleton<FilenameRelationService>();
        services.AddSingleton<ITagHistoryService, TagHistoryService>();
        services.AddTransient<MainForm>();
        services.AddTransient<SettingsForm>();
        services.AddTransient<AboutDialog>();
        services.AddTransient<TagHistoryForm>();
        services.AddTransient<PictureSearchForm>();
        services.AddTransient<LyricsSearchForm>();
    }
}
