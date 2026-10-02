namespace BsxtBatch.Core.Models;

/// <summary>Which source files a batch opens. <see cref="Both"/> is every supported
/// input (RAW, JPEG, TIFF, PSD). The other modes keep only that camera format.</summary>
public enum InputProcessMode
{
    /// <summary>RAW, JPEG, TIFF, and PSD.</summary>
    Both,

    /// <summary>Camera RAW only (ARW, CR2, CR3, NEF, DNG, and the other RAW extensions).</summary>
    RawsOnly,

    /// <summary>JPEG only (.jpg, .jpeg, .jpe).</summary>
    JpegsOnly,
}

/// <summary>Labels and the allow/skip rules for <see cref="InputProcessMode"/>.</summary>
public static class InputProcessModeInfo
{
    public static bool Allows(InputProcessMode mode, FileKind kind) => mode switch
    {
        InputProcessMode.RawsOnly => kind == FileKind.Raw,
        InputProcessMode.JpegsOnly => kind == FileKind.Jpeg,
        _ => kind is FileKind.Raw or FileKind.Jpeg or FileKind.Tiff or FileKind.Psd,
    };

    /// <summary>Text for the Process dropdown and the batch log.</summary>
    public static string Label(InputProcessMode mode) => mode switch
    {
        InputProcessMode.RawsOnly => "RAWs only",
        InputProcessMode.JpegsOnly => "JPGs only",
        _ => "Both",
    };

    /// <summary>Parses <c>raw</c>, <c>jpg</c>, or <c>both</c> (CLI).</summary>
    public static InputProcessMode Parse(string text)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "raw":
            case "raws":
            case "raws only":
                return InputProcessMode.RawsOnly;
            case "jpg":
            case "jpeg":
            case "jpgs":
            case "jpgs only":
                return InputProcessMode.JpegsOnly;
            case "both":
            case "all":
                return InputProcessMode.Both;
            default:
                throw new ArgumentException(
                    $"Unknown process mode '{text}'. Use raw, jpg, or both.");
        }
    }
}
