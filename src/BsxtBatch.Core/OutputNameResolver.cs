using BsxtBatch.Core.Models;

namespace BsxtBatch.Core;

/// <summary>Builds output paths: same folder as the source, "-BSXT" appended to the
/// file name, extension taken from the export format the user selected (JPG → .jpg,
/// TIF → .tif, PSD → .psd). One output path per selected format.</summary>
public static class OutputNameResolver
{
    public const string Suffix = "-BSXT";

    /// <summary>The single output path for one export format.</summary>
    public static string Resolve(string sourcePath, ExportFormat format)
    {
        var dir = Path.GetDirectoryName(sourcePath)
                  ?? throw new ArgumentException($"'{sourcePath}' is not a file path.", nameof(sourcePath));

        var name = Path.GetFileNameWithoutExtension(sourcePath);
        return Path.Combine(dir, name + Suffix + ExportOptions.ExtensionFor(format));
    }

    /// <summary>All output paths for the selected formats, in canonical write order.</summary>
    public static IReadOnlyList<string> ResolveAll(string sourcePath, ExportOptions options)
    {
        if (!options.AnySelected)
            throw new ArgumentException("At least one export format must be selected.", nameof(options));

        return options.Selected().Select(f => Resolve(sourcePath, f)).ToList();
    }

    /// <summary>Guards against feeding the tool its own outputs (photo-BSXT.tif →
    /// photo-BSXT-BSXT.tif) and against a source that already carries the suffix.</summary>
    public static bool LooksLikeBsxtOutput(string sourcePath)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        return name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase);
    }
}
