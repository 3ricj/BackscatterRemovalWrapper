using BsxtBatch.Core;
using BsxtBatch.Core.Models;

namespace BsxtBatch.Core.Tests;

public class OutputNameResolverTests
{
    // Windows-only app: Path.Combine normalizes separators, so expectations use Windows paths.
    [Theory]
    [InlineData(ExportFormat.Jpeg, @"C:\pics\photo-BSXT.jpg")]
    [InlineData(ExportFormat.Tiff, @"C:\pics\photo-BSXT.tif")]
    [InlineData(ExportFormat.Psd, @"C:\pics\photo-BSXT.psd")]
    public void Resolve_per_format_uses_source_folder_and_extension(ExportFormat format, string expected)
        => Assert.Equal(expected, OutputNameResolver.Resolve(@"C:\pics\photo.jpg", format));

    [Theory]
    [InlineData(@"C:\pics\photo.JPEG", ExportFormat.Tiff, @"C:\pics\photo-BSXT.tif")]
    [InlineData(@"C:\pics\scan.tiff", ExportFormat.Psd, @"C:\pics\scan-BSXT.psd")]
    [InlineData(@"C:\pics\master.psd", ExportFormat.Tiff, @"C:\pics\master-BSXT.tif")]
    [InlineData(@"C:\pics\_DSC2081.ARW", ExportFormat.Tiff, @"C:\pics\_DSC2081-BSXT.tif")]
    [InlineData(@"D:\a\b\pic.cr3", ExportFormat.Jpeg, @"D:\a\b\pic-BSXT.jpg")]
    public void Resolve_handles_every_input_kind(string source, ExportFormat format, string expected)
        => Assert.Equal(expected, OutputNameResolver.Resolve(source, format));

    [Fact]
    public void ResolveAll_returns_every_selected_format_in_canonical_order()
    {
        var options = new ExportOptions { Formats = ExportFormat.Jpeg | ExportFormat.Tiff | ExportFormat.Psd };

        var result = OutputNameResolver.ResolveAll(@"C:\pics\photo.arw", options);

        Assert.Equal(
            [@"C:\pics\photo-BSXT.tif", @"C:\pics\photo-BSXT.psd", @"C:\pics\photo-BSXT.jpg"],
            result);
    }

    [Fact]
    public void ResolveAll_single_format_returns_single_path()
    {
        var result = OutputNameResolver.ResolveAll(@"C:\pics\photo.jpg", ExportOptions.DefaultTiff);

        Assert.Single(result);
        Assert.Equal(@"C:\pics\photo-BSXT.tif", result[0]);
    }

    [Fact]
    public void ResolveAll_rejects_empty_selection()
        => Assert.ThrowsAny<ArgumentException>(
            () => OutputNameResolver.ResolveAll(@"C:\pics\photo.jpg", new ExportOptions { Formats = ExportFormat.None }));

    [Theory]
    [InlineData(@"C:\pics\photo-BSXT.tif", true)]
    [InlineData(@"C:\pics\photo-BSXT.jpg", true)]
    [InlineData(@"C:\pics\photo-BSXT.psd", true)]
    [InlineData(@"C:\pics\photo-bsxt.tif", true)]    // case-insensitive
    [InlineData(@"C:\pics\basxt.tif", false)]        // not a suffix
    [InlineData(@"C:\pics\photoBSXT.tif", false)]    // old naming, no hyphen — not our output
    [InlineData(@"C:\pics\photo.tif", false)]
    public void LooksLikeBsxtOutput_detects_previous_outputs(string source, bool expected)
        => Assert.Equal(expected, OutputNameResolver.LooksLikeBsxtOutput(source));

    [Fact]
    public void Resolve_rejects_directoryless_path()
        => Assert.ThrowsAny<ArgumentException>(() => OutputNameResolver.Resolve("", ExportFormat.Tiff));
}
