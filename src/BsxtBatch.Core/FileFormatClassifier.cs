using BsxtBatch.Core.Models;

namespace BsxtBatch.Core;

/// <summary>Classifies source files by extension. Only the extension matters for
/// routing (JPEG export vs TIFF export); the file's actual format is whatever
/// Photoshop accepts when it opens it.</summary>
public static class FileFormatClassifier
{
    private static readonly HashSet<string> JpegExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".jpe" };

    private static readonly HashSet<string> TiffExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".tif", ".tiff" };

    private static readonly HashSet<string> PsdExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".psd", ".psb" };

    /// <summary>RAW extensions handled by Camera Raw. CR3 support depends on the
    /// installed Camera Raw version; unsupported bodies fail at Open with a clear error.</summary>
    private static readonly HashSet<string> RawExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".arw", ".srf", ".sr2",          // Sony
            ".cr2", ".cr3",                  // Canon
            ".nef", ".nrw",                  // Nikon
            ".raf",                          // Fuji
            ".orf", ".rw2",                  // Olympus / Panasonic
            ".dng",                          // Adobe Digital Negative
            ".raw",                          // generic
        };

    public static FileKind Classify(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
            return FileKind.Unsupported;

        if (JpegExtensions.Contains(ext)) return FileKind.Jpeg;
        if (TiffExtensions.Contains(ext)) return FileKind.Tiff;
        if (PsdExtensions.Contains(ext)) return FileKind.Psd;
        if (RawExtensions.Contains(ext)) return FileKind.Raw;
        return FileKind.Unsupported;
    }

    /// <summary>JPEG in → JPEG out; everything else (RAW/TIFF/PSD) → TIFF, per the
    /// approved design decision to avoid lossy re-encode of camera data.</summary>
    public static bool ExportsAsTiff(FileKind kind) => kind is not FileKind.Jpeg;

    /// <summary>True when the picker/importer should offer this file.</summary>
    public static bool IsSupported(string path) => Classify(path) != FileKind.Unsupported;
}
