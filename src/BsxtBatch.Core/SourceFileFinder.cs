namespace BsxtBatch.Core;

/// <summary>Lists supported images under a folder, including every subfolder.
/// Junctions and other reparse points are not followed. Inaccessible directories
/// are skipped.</summary>
public static class SourceFileFinder
{
    public static IReadOnlyList<string> EnumerateSupportedFiles(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
        };

        return Directory.EnumerateFiles(root, "*", options)
            .Where(FileFormatClassifier.IsSupported)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
