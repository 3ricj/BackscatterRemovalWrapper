using BsxtBatch.Core.Models;

namespace BsxtBatch.Core.Tests;

public class ExportOptionsTests
{
    [Fact]
    public void Default_is_Tiff_only()
    {
        Assert.True(ExportOptions.DefaultTiff.Formats.HasFlag(ExportFormat.Tiff));
        Assert.False(ExportOptions.DefaultTiff.Formats.HasFlag(ExportFormat.Jpeg));
        Assert.False(ExportOptions.DefaultTiff.Formats.HasFlag(ExportFormat.Psd));
        Assert.True(ExportOptions.DefaultTiff.AnySelected);
    }

    [Fact]
    public void New_instance_defaults_to_Tiff()
        => Assert.Equal(ExportFormat.Tiff, new ExportOptions().Formats);

    [Fact]
    public void None_is_not_a_valid_selection()
        => Assert.False(new ExportOptions { Formats = ExportFormat.None }.AnySelected);

    [Fact]
    public void Selected_returns_canonical_write_order()
    {
        var options = new ExportOptions { Formats = ExportFormat.Jpeg | ExportFormat.Tiff | ExportFormat.Psd };

        Assert.Equal([ExportFormat.Tiff, ExportFormat.Psd, ExportFormat.Jpeg], options.Selected());
    }

    [Theory]
    [InlineData(ExportFormat.Jpeg, ".jpg")]
    [InlineData(ExportFormat.Tiff, ".tif")]
    [InlineData(ExportFormat.Psd, ".psd")]
    public void ExtensionFor_maps_formats(ExportFormat format, string expected)
        => Assert.Equal(expected, ExportOptions.ExtensionFor(format));

    [Theory]
    [InlineData("tif", ExportFormat.Tiff)]
    [InlineData("TIFF", ExportFormat.Tiff)]
    [InlineData("jpg", ExportFormat.Jpeg)]
    [InlineData("jpeg", ExportFormat.Jpeg)]
    [InlineData("psd", ExportFormat.Psd)]
    [InlineData("jpg,tif", ExportFormat.Jpeg | ExportFormat.Tiff)]
    [InlineData("tif psd", ExportFormat.Tiff | ExportFormat.Psd)]
    [InlineData("psd, jpg, tif", ExportFormat.Jpeg | ExportFormat.Tiff | ExportFormat.Psd)]
    public void FromCsv_parses_user_lists(string csv, ExportFormat expected)
        => Assert.Equal(expected, ExportOptions.FromCsv(csv).Formats);

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("bmp")]
    public void FromCsv_rejects_empty_or_unknown(string csv)
        => Assert.ThrowsAny<ArgumentException>(() => ExportOptions.FromCsv(csv));
}
