namespace BsxtBatch.Core.Models;

/// <summary>What kind of image file a source path is, decided purely by extension.</summary>
public enum FileKind
{
    /// <summary>JPEG (jpg/jpeg) — exported back out as JPEG.</summary>
    Jpeg,

    /// <summary>Tagged Image File Format.</summary>
    Tiff,

    /// <summary>Photoshop document.</summary>
    Psd,

    /// <summary>A camera RAW file (arw, cr3, nef, raf, orw, rw2, dng, ...). Opened through Camera Raw.</summary>
    Raw,

    /// <summary>Anything else — rejected before touching Photoshop.</summary>
    Unsupported,
}
