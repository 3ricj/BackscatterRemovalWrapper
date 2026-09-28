namespace BsxtBatch.Core.Models;

/// <summary>Bit flags for the export formats a batch run writes per file.</summary>
[Flags]
public enum ExportFormat
{
    None = 0,
    Jpeg = 1,
    Tiff = 2,
    Psd = 4,
}

/// <summary>
/// Which output formats the batch writes for each processed file. At least one
/// format must be selected; the default is TIFF only (the historical behaviour).
/// When <see cref="ExportFormat.Psd"/> is selected the saved document keeps the
/// BSXT action's layers (a layered copy, never flattened).
/// </summary>
public sealed class ExportOptions
{
    public static ExportOptions DefaultTiff { get; } = new() { Formats = ExportFormat.Tiff };

    public ExportFormat Formats { get; set; } = ExportFormat.Tiff;

    /// <summary>Folder all outputs are written to. Null/empty (the default) means
    /// "same folder as each source file"; set it to redirect every export elsewhere.</summary>
    public string? OutputDirectory { get; set; }

    /// <summary>True when outputs are redirected away from the source folders.</summary>
    public bool HasOutputOverride => !string.IsNullOrWhiteSpace(OutputDirectory);

    public bool AnySelected => Formats != ExportFormat.None;

    /// <summary>Selected formats in canonical write order: TIF, then PSD, then JPG.</summary>
    public IEnumerable<ExportFormat> Selected()
    {
        if (Formats.HasFlag(ExportFormat.Tiff)) yield return ExportFormat.Tiff;
        if (Formats.HasFlag(ExportFormat.Psd)) yield return ExportFormat.Psd;
        if (Formats.HasFlag(ExportFormat.Jpeg)) yield return ExportFormat.Jpeg;
    }

    public static string ExtensionFor(ExportFormat format) => format switch
    {
        ExportFormat.Jpeg => ".jpg",
        ExportFormat.Tiff => ".tif",
        ExportFormat.Psd => ".psd",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown export format."),
    };

    /// <summary>Parses a comma/space-separated format list (used by the CLI).
    /// Accepts jpg/jpeg, tif/tiff, psd. Throws when nothing valid is selected.</summary>
    public static ExportOptions FromCsv(string csv)
    {
        var formats = ExportFormat.None;
        foreach (var token in csv.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            formats |= token.Trim().ToLowerInvariant() switch
            {
                "jpg" or "jpeg" => ExportFormat.Jpeg,
                "tif" or "tiff" => ExportFormat.Tiff,
                "psd" => ExportFormat.Psd,
                var other => throw new ArgumentException($"Unknown export format '{other}'. Use jpg, tif and/or psd."),
            };
        }

        if (formats == ExportFormat.None)
            throw new ArgumentException("At least one export format must be selected (jpg, tif, psd).");

        return new ExportOptions { Formats = formats };
    }
}
