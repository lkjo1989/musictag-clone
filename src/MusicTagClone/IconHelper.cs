using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using FontAwesome.Sharp;

namespace MusicTagClone;

/// <summary>
/// 图标工具类 — 使用 FontAwesome.Sharp 生成 16x16 图标 Bitmap。
/// </summary>
internal static class IconHelper
{
    /// <summary>歌词编辑图标（铅笔，与顶部工具栏「标签编辑」组同蓝）</summary>
    public static Bitmap GetLyricIcon() => RenderIcon(IconChar.PencilAlt, ToolBlue);

    /// <summary>编码修正图标（扳手，与顶部工具栏「标签编辑」组同蓝）</summary>
    public static Bitmap GetCharsetIcon() => RenderIcon(IconChar.Wrench, ToolBlue);

    /// <summary>顶部工具栏「标签编辑」组蓝色</summary>
    private static readonly Color ToolBlue = Color.FromArgb(21, 101, 192);

    /// <summary>工具栏快捷图标（16x16，按图标语义配色，避免整栏黑白）</summary>
    public static Bitmap GetToolIcon(IconChar icon)
        => RenderIcon(icon, ToolIconColors.TryGetValue(icon, out var color) ? color : DarkGray);

    /// <summary>工具栏图标配色 — 按分隔线分组，每组一个颜色（与工具栏分组一致）；未列出的图标回退深灰。</summary>
    private static readonly Dictionary<IconChar, Color> ToolIconColors = new()
    {
        // 第一组：文件/目录（文件夹黄）
        [IconChar.FolderOpen] = Color.FromArgb(249, 168, 37),   // 改变工作目录
        [IconChar.FolderPlus] = Color.FromArgb(249, 168, 37),   // 添加目录
        [IconChar.Folder] = Color.FromArgb(249, 168, 37),       // 管理目录

        // 第二组：标签编辑（蓝）
        [IconChar.Save] = ToolBlue,         // 保存标签
        [IconChar.Eraser] = ToolBlue,       // 清除标签
        [IconChar.Undo] = ToolBlue,         // 撤销
        [IconChar.Tags] = ToolBlue,         // 读取标签
        [IconChar.Wrench] = ToolBlue,       // 编码修正
        [IconChar.ExchangeAlt] = ToolBlue,  // 简繁转换
        [IconChar.History] = ToolBlue,      // 标签历史

        // 第三组：选择（绿）
        [IconChar.CheckDouble] = Color.FromArgb(46, 125, 50),   // 全选
        [IconChar.Square] = Color.FromArgb(46, 125, 50),        // 取消选定

        // 第四组：标签源（紫）；刷新单独配色（青）
        [IconChar.SyncAlt] = Color.FromArgb(0, 131, 143),           // 刷新（单独配色）
        [IconChar.Image] = Color.FromArgb(123, 31, 162),            // 图片源
        [IconChar.Music] = Color.FromArgb(123, 31, 162),            // 歌词源
        [IconChar.CloudDownloadAlt] = Color.FromArgb(123, 31, 162), // 组合标签源

        // 第五组：批量（橙）；自动匹配标签单独配色（品红）
        [IconChar.Magic] = Color.FromArgb(194, 24, 91),             // 自动匹配标签（单独配色）
        [IconChar.FileAlt] = Color.FromArgb(230, 81, 25),           // 另存歌词为LRC
        [IconChar.FileImage] = Color.FromArgb(230, 81, 25),         // 提取封面
        [IconChar.FileSignature] = Color.FromArgb(230, 81, 25),     // 文件名相关

        // 第六组：设置（蓝灰）
        [IconChar.Cog] = Color.FromArgb(84, 110, 122),          // 设置
    };

    private static readonly Color DarkGray = Color.FromArgb(96, 98, 102);

    internal static Bitmap RenderIcon(IconChar icon, Color color, int iconSize = 14, int bitmapSize = 16)
        => RenderIcon(icon, color, Color.Transparent, iconSize, bitmapSize);

    internal static Bitmap RenderIcon(IconChar icon, Color color, Color bgColor, int iconSize = 14, int bitmapSize = 16)
    {
        using var pic = new IconPictureBox
        {
            IconChar = icon,
            IconColor = color,
            IconSize = iconSize,
            BackColor = bgColor,
            Size = new Size(bitmapSize, bitmapSize)
        };
        // 强制创建句柄，确保 DrawToBitmap 可用
        _ = pic.Handle;
        var bmp = new Bitmap(bitmapSize, bitmapSize);
        pic.DrawToBitmap(bmp, new Rectangle(0, 0, bitmapSize, bitmapSize));
        return bmp;
    }

    /// <summary>
    /// 从图片字节数据读取原图尺寸并生成显示尺寸的缩略图位图。
    /// 高质量双三次下采样一次到位；比解码全尺寸位图省内存、快得多。
    /// 返回的 Bitmap 独立于源流，调用方负责 Dispose；图片无法解析时尺寸返回 0。
    /// </summary>
    /// <param name="imageData">原始图片字节（JPEG/PNG/BMP/GIF 等）。</param>
    /// <param name="maxSize">缩略图最长边像素。默认 256（2x of 170 显示尺寸，给 DPI/缩放留余量）。</param>
    public static (Bitmap? Thumbnail, int Width, int Height) CreateThumbnailWithResolution(
        byte[]? imageData, int maxSize = 256)
    {
        if (imageData == null || imageData.Length == 0) return (null, 0, 0);
        try
        {
            using var ms = new MemoryStream(imageData);
            // validateImageData=false：只读头部确定格式/尺寸即可加载，跳过全像素校验，更快
            using var src = Image.FromStream(ms, false, false);
            // 始终复制一份独立位图，避免持有 stream-bound 的原图（src 随 using 释放会撤销其上的 Bitmap）
            int origWidth = src.Width, origHeight = src.Height;
            int w = origWidth, h = origHeight;
            if (w > maxSize || h > maxSize)
            {
                double ratio = Math.Min((double)maxSize / w, (double)maxSize / h);
                w = Math.Max(1, (int)(w * ratio));
                h = Math.Max(1, (int)(h * ratio));
            }

            var thumb = new Bitmap(w, h);
            try
            {
                using var g = Graphics.FromImage(thumb);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                // 透明背景图（PNG/GIF）合成到白底，避免黑边
                g.Clear(Color.White);
                g.DrawImage(src, 0, 0, w, h);
                return (thumb, origWidth, origHeight);
            }
            catch
            {
                thumb.Dispose();
                throw;
            }
        }
        catch
        {
            return (null, 0, 0);
        }
    }

    /// <summary>从图片字节数据生成缩略图，调用方负责 Dispose 返回的 Bitmap。</summary>
    public static Bitmap? CreateThumbnail(byte[]? imageData, int maxSize = 256)
        => CreateThumbnailWithResolution(imageData, maxSize).Thumbnail;
}
