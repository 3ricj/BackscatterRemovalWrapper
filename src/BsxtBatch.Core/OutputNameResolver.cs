using BsxtBatch.Core.Models;

namespace BsxtBatch.Core;

/// <summary>Builds the output path: same folder as the source, "-BSXT" appended to the
/// file name, extension normalized per the approved format policy (JPG → .jpg,
/// everything else → .tif).</summary>
public static class OutputNameResolver
{
    public const string Suffix = "-BSXT";

    public static string Resolve(string sourcePath)
    {
        var dir = Path.GetDirectoryName(sourcePath)
                  ?? throw new ArgumentException($"'{sourcePath}' is not a file path.", nameof(sourcePath));

        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var kind = FileFormatClassifier.Classify(sourcePath);
        var outExt = FileFormatClassifier.ExportsAsTiff(kind) ? ".tif" : ".jpg";

        return Path.Combine(dir, name + Suffix + outExt);
    }

    /// <summary>Guards against feeding the tool its own outputs (photo-BSXT.tif →
    /// photo-BSXT-BSXT.tif) and against a source that already carries the suffix.</summary>
    public static bool LooksLikeBsxtOutput(string sourcePath)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        return name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase);
    }
}
