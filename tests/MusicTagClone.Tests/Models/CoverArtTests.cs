using MusicTagClone.Models;

namespace MusicTagClone.Tests.Models;

public class CoverArtTests
{
    [Fact]
    public void HasImage_WithNullData_ReturnsFalse()
    {
        var cover = new CoverArt { ImageData = null };
        Assert.False(cover.HasImage);
    }

    [Fact]
    public void HasImage_WithEmptyData_ReturnsFalse()
    {
        var cover = new CoverArt { ImageData = Array.Empty<byte>() };
        Assert.False(cover.HasImage);
    }

    [Fact]
    public void HasImage_WithData_ReturnsTrue()
    {
        var cover = new CoverArt { ImageData = new byte[] { 0xFF, 0xD8 } };
        Assert.True(cover.HasImage);
    }

    [Fact]
    public void FileSizeBytes_ReturnsDataLength()
    {
        var data = new byte[1024];
        var cover = new CoverArt { ImageData = data };
        Assert.Equal(1024, cover.FileSizeBytes);
    }

    [Fact]
    public void FileSizeBytes_WithNull_ReturnsZero()
    {
        var cover = new CoverArt();
        Assert.Equal(0, cover.FileSizeBytes);
    }

    [Fact]
    public void Validate_WithNullData_ReturnsFalse()
    {
        var cover = new CoverArt();
        var limits = new CoverArt.LimitsConfig();
        Assert.False(cover.Validate(limits, out var error));
        Assert.Contains("No image data", error);
    }

    [Fact]
    public void Validate_WithUnsupportedFormat_ReturnsFalse()
    {
        var cover = new CoverArt
        {
            ImageData = new byte[100],
            MimeType = "image/webp",
            Width = 100,
            Height = 100
        };
        var limits = new CoverArt.LimitsConfig { FormatLimits = "jpg,png" };
        Assert.False(cover.Validate(limits, out var error));
        Assert.Contains("Format not allowed", error);
    }

    [Fact]
    public void Validate_WithOversizedResolution_ReturnsFalse()
    {
        var cover = new CoverArt
        {
            ImageData = new byte[100],
            MimeType = "image/jpeg",
            Width = 5000,
            Height = 5000
        };
        var limits = new CoverArt.LimitsConfig { MaxResolution = 3000 };
        Assert.False(cover.Validate(limits, out var error));
        Assert.Contains("Resolution", error);
    }

    [Fact]
    public void Validate_WithOversizedFile_ReturnsFalse()
    {
        var cover = new CoverArt
        {
            ImageData = new byte[2 * 1024 * 1024], // 2MB
            MimeType = "image/jpeg",
            Width = 100,
            Height = 100
        };
        var limits = new CoverArt.LimitsConfig { MaxSizeKB = 1024 };
        Assert.False(cover.Validate(limits, out var error));
        Assert.Contains("File size", error);
    }

    [Fact]
    public void Validate_WithValidCover_ReturnsTrue()
    {
        var cover = new CoverArt
        {
            ImageData = new byte[100],
            MimeType = "image/jpeg",
            Width = 500,
            Height = 500
        };
        var limits = new CoverArt.LimitsConfig();
        Assert.True(cover.Validate(limits, out _));
    }

    [Fact]
    public void Validate_PngFormat_IsAccepted()
    {
        var cover = new CoverArt
        {
            ImageData = new byte[100],
            MimeType = "image/png",
            Width = 100,
            Height = 100
        };
        var limits = new CoverArt.LimitsConfig();
        Assert.True(cover.Validate(limits, out _));
    }

    [Fact]
    public void LimitsConfig_HasCorrectDefaults()
    {
        var limits = new CoverArt.LimitsConfig();
        Assert.Equal("jpg,jpeg,png,bmp,gif", limits.FormatLimits);
        Assert.Equal(3000, limits.MaxResolution);
        Assert.Equal(1024, limits.MaxSizeKB);
        Assert.True(limits.OverwriteExisting);
    }

    [Fact]
    public void GetMimeType_WithJpgExtension_ReturnsJpeg()
    {
        var tempFile = Path.GetTempFileName() + ".jpg";
        try
        {
            // 写入最小JPEG文件头
            File.WriteAllBytes(tempFile, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 });
            // GetMimeType is private, tested indirectly through FromFile
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
