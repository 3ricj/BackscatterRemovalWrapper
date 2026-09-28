using BsxtBatch.Core.Models;

namespace BsxtBatch.Core;

public enum BatchOutcome
{
    Completed,

    /// <summary>The BSXT action/set was missing; the remaining queue was abandoned.</summary>
    AbortedMissingAction,

    /// <summary>The user cancelled; remaining jobs stay Queued.</summary>
    Cancelled,
}

public sealed record BatchRunResult(BatchOutcome Outcome, int Completed, int Skipped, int Failed, string? FatalError = null);

/// <summary>
/// Runs a batch of <see cref="BatchJob"/>s sequentially on a dedicated STA thread
/// (Photoshop COM calls must come from a single-threaded apartment). Every file is
/// error-isolated: one failure never stops the rest — except a missing action set,
/// which is fatal for the whole batch because every remaining file would fail the same way.
/// </summary>
public sealed class BatchProcessor
{
    /// <summary>Raised before a job starts processing (worker thread — marshal to UI).</summary>
    public event Action<BatchJob>? JobStarting;

    /// <summary>Raised after a job finishes in any state (worker thread — marshal to UI).</summary>
    public event Action<BatchJob>? JobFinished;

    /// <summary>Free-form log lines for the UI log pane (worker thread — marshal to UI).</summary>
    public event Action<string>? Log;

    /// <summary>
    /// Processes the given jobs, exporting each processed file in every format selected
    /// in <paramref name="export"/> (at least one must be selected). Returns when the
    /// queue drains, is cancelled, or the action proves missing. Never throws for
    /// per-file problems — those land on the job.
    /// </summary>
    public Task<BatchRunResult> RunAsync(IReadOnlyList<BatchJob> jobs, ExportOptions export, CancellationToken ct = default)
    {
        // Photoshop COM demands an STA; run the whole loop on its own thread and surface
        // the result through the Task returned to the caller.
        var tcs = new TaskCompletionSource<BatchRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var worker = new Thread(() =>
        {
            try { tcs.SetResult(Run(jobs, export, ct)); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        {
            Name = "BsxtBatch.STA",
            IsBackground = true,
        };

        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return tcs.Task;
    }

    private BatchRunResult Run(IReadOnlyList<BatchJob> jobs, ExportOptions export, CancellationToken ct)
    {
        if (!export.AnySelected)
            throw new ArgumentException("At least one export format must be selected.", nameof(export));

        int completed = 0, skipped = 0, failed = 0;

        // Pre-flight (no Photoshop needed): reject unsupported kinds, our own outputs,
        // and folders we cannot write to, so obvious skips don't cost an Open cycle.
        var runnable = new List<BatchJob>();
        foreach (var job in jobs)
        {
            if (job.Status is not JobStatus.Queued)
                continue;

            if (job.Kind == FileKind.Unsupported)
            {
                Skip(job, "Unsupported file type.");
                skipped++;
                continue;
            }

            if (OutputNameResolver.LooksLikeBsxtOutput(job.SourcePath))
            {
                Skip(job, "File name already ends with '-BSXT' — looks like a previous output; not processed again.");
                skipped++;
                continue;
            }

            var dir = Path.GetDirectoryName(job.SourcePath) ?? "";
            if (!File.Exists(job.SourcePath))
            {
                Skip(job, "File not found.");
                skipped++;
                continue;
            }

            // Never overwrite: every selected output that already exists would be
            // clobbered. Only when ALL selected outputs exist is the file skipped;
            // when just some exist, the missing formats are still written (see Process).
            var planned = OutputNameResolver.ResolveAll(job.SourcePath, export);
            if (planned.All(File.Exists))
            {
                var names = string.Join("', '", planned.Select(Path.GetFileName));
                Skip(job, $"Output(s) '{names}' already exist — not overwriting. Delete or rename them first.");
                skipped++;
                continue;
            }

            if (!DirectoryHasWriteAccess(dir))
            {
                Skip(job, $"No write permission in '{dir}' — cannot place the output there.");
                skipped++;
                continue;
            }

            runnable.Add(job);
        }

        if (runnable.Count == 0)
            return new BatchRunResult(BatchOutcome.Completed, completed, skipped, failed);

        using var ps = PhotoshopAutomationService.Attach();
        Log?.Invoke($"Connected to Photoshop {ps.Version}. {runnable.Count} file(s) queued.");

        foreach (var job in runnable)
        {
            if (ct.IsCancellationRequested)
            {
                Log?.Invoke("Cancelled — remaining files left queued.");
                return new BatchRunResult(BatchOutcome.Cancelled, completed, skipped, failed);
            }

            job.Status = JobStatus.Running;
            job.Detail = null;
            JobStarting?.Invoke(job);
            Log?.Invoke($"▶ {job.FileName}");

            try
            {
                var plannedForJob = OutputNameResolver.ResolveAll(job.SourcePath, export);
                var written = ps.Process(job, export, ct);
                var alreadyThere = plannedForJob.Except(written, StringComparer.OrdinalIgnoreCase)
                                                   .Select(p => Path.GetFileName(p)).ToList();

                job.Status = JobStatus.Completed;
                job.OutputPath = written.Count > 0 ? written[0] : null;
                job.Detail = written.Count > 0
                    ? string.Join(", ", written.Select(p => Path.GetFileName(p)))
                    : "Nothing written.";
                if (alreadyThere.Count > 0)
                    job.Detail += $" (already existed, not overwritten: {string.Join(", ", alreadyThere)})";
                completed++;
                Log?.Invoke($"✔ {job.FileName} → {job.Detail}");
            }
            catch (AlreadyProcessedException ex)
            {
                job.Status = JobStatus.Skipped;
                job.Detail = $"Already processed (contains a '{ex.OffendingLayer}' layer). Run BSXT only on un-edited files.";
                skipped++;
                Log?.Invoke($"⚠ {job.FileName}: {job.Detail}");
            }
            catch (ActionMissingException ex)
            {
                job.Status = JobStatus.Failed;
                job.Detail = ex.Message;
                failed++;
                Log?.Invoke($"✖ FATAL: {ex.Message}");
                Log?.Invoke("  Load the 'BSXT' action set in Photoshop (Actions panel ▸ Load Actions…) and start the batch again.");
                return new BatchRunResult(BatchOutcome.AbortedMissingAction, completed, skipped, failed, ex.Message);
            }
            catch (OperationCanceledException)
            {
                job.Status = JobStatus.Queued;
                Log?.Invoke($"■ {job.FileName} cancelled.");
                return new BatchRunResult(BatchOutcome.Cancelled, completed, skipped, failed);
            }
            catch (Exception ex)
            {
                job.Status = JobStatus.Failed;
                job.Detail = ex.Message;
                failed++;
                Log?.Invoke($"✖ {job.FileName}: {ex.Message}");
            }

            JobFinished?.Invoke(job);
        }

        return new BatchRunResult(BatchOutcome.Completed, completed, skipped, failed);

        void Skip(BatchJob job, string reason)
        {
            job.Status = JobStatus.Skipped;
            job.Detail = reason;
            JobFinished?.Invoke(job);
            Log?.Invoke($"⏭ {job.FileName}: {reason}");
        }
    }

    private static bool DirectoryHasWriteAccess(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return false;
        try
        {
            var probe = Path.Combine(dir, $".bsxtwrite_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
