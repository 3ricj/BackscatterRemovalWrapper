namespace BsxtBatch.Core.Models;

public enum JobStatus
{
    Queued,
    Running,
    Completed,

    /// <summary>Skipped by policy (e.g. the file already contains BSXT/Cleanup layers —
    /// re-running the action on such a document fails with "command Make is not available").</summary>
    Skipped,

    Failed,
}
