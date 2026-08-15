namespace MusicTagClone.Models;

/// <summary>
/// 封面图片信息，包含图片数据、尺寸和MIME类型
/// </summary>
public class CoverArt
{
    public byte[]? ImageData { get; set; }
    public string? MimeType { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long FileSizeBytes => ImageData?.Length ?? 0;
    public bool HasImage => ImageData is { Length: > 0 };

    /// <summary>
    /// 封面格式/大小限制配置
    /// </summary>
    public class LimitsConfig
    {
        /// <summary>支持的图片格式（如 "jpg,png,bmp"）</summary>
        public string FormatLimits { get; set; } = "jpg,jpeg,png,bmp,gif";

        /// <summary>最大分辨率（宽或高）</summary>
        public int MaxResolution { get; set; } = 3000;

        /// <summary>最大文件大小 (KB)</summary>
        public int MaxSizeKB { get; set; } = 1024;

        /// <summary>是否覆盖已有封面</summary>
        public bool OverwriteExisting { get; set; } = true;
    }

    /// <summary>
    /// 从文件加载封面图片
    /// </summary>
    public static CoverArt FromFile(string imagePath)
    {
        var data = File.ReadAllBytes(imagePath);
        var mime = GetMimeType(imagePath);
        return new CoverArt { ImageData = data, MimeType = mime };
    }

    private static string GetMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// 验证是否在限制范围内
    /// </summary>
    public bool Validate(LimitsConfig limits, out string errorMessage)
    {
        if (!HasImage)
        {
            errorMessage = "No image data";
            return false;
        }

        var ext = MimeType switch
        {
            "image/jpeg" => "jpg",
            "image/png" => "png",
            "image/bmp" => "bmp",
            "image/gif" => "gif",
            _ => ""
        };

        if (string.IsNullOrEmpty(ext) || limits.FormatLimits.IndexOf(ext, StringComparison.OrdinalIgnoreCase) < 0)
        {
            errorMessage = $"Format not allowed: {MimeType}. Allowed: {limits.FormatLimits}";
            return false;
        }

        if (Width > limits.MaxResolution || Height > limits.MaxResolution)
        {
            errorMessage = $"Resolution {Width}x{Height} exceeds limit {limits.MaxResolution}";
            return false;
        }

        if (FileSizeBytes / 1024 > limits.MaxSizeKB)
        {
            errorMessage = $"File size {FileSizeBytes / 1024}KB exceeds limit {limits.MaxSizeKB}KB";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }
}
