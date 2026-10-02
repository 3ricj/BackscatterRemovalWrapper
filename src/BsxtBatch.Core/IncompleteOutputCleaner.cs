namespace BsxtBatch.Core;

/// <summary>Deletes outputs a failed attempt created but did not finish writing.
/// Completed saves and files that already existed before the attempt are kept,
/// so a retry cannot treat a truncated TIFF as a finished result.</summary>
public static class IncompleteOutputCleaner
{
    public static void DeleteUnfinished(
        IEnumerable<string> planned,
        ISet<string> existedBefore,
        IReadOnlyCollection<string> completed)
    {
        var done = new HashSet<string>(completed, StringComparer.OrdinalIgnoreCase);
        foreach (var path in planned)
        {
            if (done.Contains(path) || existedBefore.Contains(path))
                continue;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best effort: a locked partial is still better reported than swallowed
                // into a false "already exists" skip on the next attempt.
            }
        }
    }
}
