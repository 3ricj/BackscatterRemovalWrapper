using BsxtBatch.Core;
using BsxtBatch.Core.Models;

namespace BsxtBatch.Core.Tests;

public class FileFormatClassifierTests
{
    [Theory]
    [InlineData("photo.jpg", FileKind.Jpeg)]
    [InlineData("photo.JPEG", FileKind.Jpeg)]
    [InlineData("scan.tif", FileKind.Tiff)]
    [InlineData("scan.TIFF", FileKind.Tiff)]
    [InlineData("master.psd", FileKind.Psd)]
    [InlineData("master.psb", FileKind.Psd)]
    [InlineData("_DSC2081.ARW", FileKind.Raw)]
    [InlineData("image.CR3", FileKind.Raw)]
    [InlineData("shot.NEF", FileKind.Raw)]
    [InlineData("shot.raf", FileKind.Raw)]
    [InlineData("shot.orf", FileKind.Raw)]
    [InlineData("shot.rw2", FileKind.Raw)]
    [InlineData("converted.dng", FileKind.Raw)]
    [InlineData("notes.txt", FileKind.Unsupported)]
    [InlineData("archive.zip", FileKind.Unsupported)]
    [InlineData("noextension", FileKind.Unsupported)]
    public void Classify_maps_extensions(string path, FileKind expected)
        => Assert.Equal(expected, FileFormatClassifier.Classify(path));

    [Theory]
    [InlineData(FileKind.Jpeg, false)]
    [InlineData(FileKind.Tiff, true)]
    [InlineData(FileKind.Psd, true)]
    [InlineData(FileKind.Raw, true)]
    public void ExportsAsTiff_only_jpeg_exports_jpeg(FileKind kind, bool expected)
        => Assert.Equal(expected, FileFormatClassifier.ExportsAsTiff(kind));

    [Fact]
    public void IsSupported_matches_classify()
    {
        Assert.True(FileFormatClassifier.IsSupported("a.ARW"));
        Assert.False(FileFormatClassifier.IsSupported("a.exe"));
    }
}
