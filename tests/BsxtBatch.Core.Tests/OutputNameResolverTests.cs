using BsxtBatch.Core;

namespace BsxtBatch.Core.Tests;

public class OutputNameResolverTests
{
    // Windows-only app: Path.Combine normalizes separators, so expectations use Windows paths.
    [Theory]
    [InlineData(@"C:\pics\photo.jpg", @"C:\pics\photo-BSXT.jpg")]
    [InlineData(@"C:\pics\photo.JPEG", @"C:\pics\photo-BSXT.jpg")]
    [InlineData(@"C:\pics\scan.tif", @"C:\pics\scan-BSXT.tif")]
    [InlineData(@"C:\pics\scan.tiff", @"C:\pics\scan-BSXT.tif")]
    [InlineData(@"C:\pics\master.psd", @"C:\pics\master-BSXT.tif")]
    [InlineData(@"C:\pics\_DSC2081.ARW", @"C:\pics\_DSC2081-BSXT.tif")]
    [InlineData(@"D:\a\b\pic.cr3", @"D:\a\b\pic-BSXT.tif")]
    public void Resolve_appends_BSXT_in_same_folder(string source, string expected)
        => Assert.Equal(expected, OutputNameResolver.Resolve(source));

    [Theory]
    [InlineData(@"C:\pics\photo-BSXT.tif", true)]
    [InlineData(@"C:\pics\photo-BSXT.jpg", true)]
    [InlineData(@"C:\pics\photo-bsxt.tif", true)]    // case-insensitive
    [InlineData(@"C:\pics\basxt.tif", false)]        // not a suffix
    [InlineData(@"C:\pics\photoBSXT.tif", false)]    // old naming, no hyphen — not our output
    [InlineData(@"C:\pics\photo.tif", false)]
    public void LooksLikeBsxtOutput_detects_previous_outputs(string source, bool expected)
        => Assert.Equal(expected, OutputNameResolver.LooksLikeBsxtOutput(source));

    [Fact]
    public void Resolve_rejects_directoryless_path()
        => Assert.ThrowsAny<ArgumentException>(() => OutputNameResolver.Resolve(""));
}
