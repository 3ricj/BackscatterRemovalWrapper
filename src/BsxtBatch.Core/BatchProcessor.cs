using BsxtBatch.Core.Models;

namespace BsxtBatch.Core;

public enum BatchOutcome
{
    Completed,

    /// <summary>The BSXT action/set was missing; the remaining queue was abandoned.</summary>
    AbortedMissingAction,

    /// <summary>The user cancelled; remaining jobs stay Queued.</summary>
    Cancelled,

    /// <summary>Photoshop hung or exited and a new instance could not be started.
    /// The current file is failed; remaining jobs stay Queued.</summary>
    AbortedPhotoshopUnavailable,
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
    public Task<BatchRunResult> RunAsync(
        IReadOnlyList<BatchJob> jobs,
        ExportOptions export,
        CancellationToken ct = default)
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

        // Output-folder override (checked at batch start, i.e. "when clicking Start"):
        // the shared target must exist (created if missing) and be writable, otherwise
        // no file could ever be written and the whole run is pointless.
        if (export.HasOutputOverride)
        {
            var dir = export.OutputDirectory!;
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Output folder '{dir}' cannot be created: {ex.Message}");
            }
            if (!DirectoryHasWriteAccess(dir))
                throw new InvalidOperationException($"No write permission in output folder '{dir}'.");
        }

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

            // With an output override the shared target was already verified once above;
            // otherwise every output lands in this job's own source folder.
            if (!export.HasOutputOverride && !DirectoryHasWriteAccess(dir))
            {
                Skip(job, $"No write permission in '{dir}' — cannot place the output there.");
                skipped++;
                continue;
            }

            runnable.Add(job);
        }

        if (runnable.Count == 0)
            return new BatchRunResult(BatchOutcome.Completed, completed, skipped, failed);

        PhotoshopAutomationService? ps = null;
        try
        {
            ps = PhotoshopAutomationService.Attach();
            var pidNote = ps.ProcessId is int pid ? $" (pid {pid})" : "";
            Log?.Invoke($"Connected to Photoshop {ps.Version}{pidNote}. {runnable.Count} file(s) queued.");
            if (ps.ProcessId is null)
                Log?.Invoke("Could not identify which Photoshop process this is. If it hangs, every Photoshop window will be closed.");
            if (ps.CrashMonitorWarning is { } warning)
                Log?.Invoke(warning);
            var timeoutMin = (int)PhotoshopAutomationService.FileAttemptTimeout.TotalMinutes;
            Log?.Invoke(
                $"A file still running after {timeoutMin} minutes is treated as a hang: Photoshop is closed, restarted, and that file is retried up to {PhotoshopAutomationService.MaxRetriesPerFile} times.");

            foreach (var job in runnable)
            {
                if (ct.IsCancellationRequested)
                {
                    Log?.Invoke("Cancelled — remaining files left queued.");
                    return new BatchRunResult(BatchOutcome.Cancelled, completed, skipped, failed);
                }

                job.Status = JobStatus.Running;
                job.Detail = "Working…";
                JobStarting?.Invoke(job);
                Log?.Invoke($"▶ {job.DisplayName}");

                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        if (ps is null)
                            throw new InvalidOperationException("Photoshop is not connected.");

                        if (attempt > 1)
                            job.Detail = $"Retry {attempt - 1} of {PhotoshopAutomationService.MaxRetriesPerFile}…";

                        var photoshop = ps;
                        var plannedForJob = OutputNameResolver.ResolveAll(job.SourcePath, export);
                        var written = photoshop.Process(job, export, ct, elapsed =>
                        {
                            var mins = Math.Max(1, (int)elapsed.TotalMinutes);
                            job.Detail = $"Working… {mins} min";
                            Log?.Invoke($"… {job.DisplayName} still working ({mins} min).");
                        });
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
                        Log?.Invoke($"✔ {job.DisplayName} → {job.Detail}");
                        break;
                    }
                    catch (AlreadyProcessedException ex)
                    {
                        job.Status = JobStatus.Skipped;
                        job.Detail = $"Already processed (contains a '{ex.OffendingLayer}' layer). Run BSXT only on un-edited files.";
                        skipped++;
                        Log?.Invoke($"⚠ {job.DisplayName}: {job.Detail}");
                        break;
                    }
                    catch (ActionMissingException ex)
                    {
                        job.Status = JobStatus.Failed;
                        job.Detail = ex.Message;
                        failed++;
                        Log?.Invoke($"✖ FATAL: {ex.Message}");
                        Log?.Invoke("  Load the 'BSXT' action set in Photoshop (Actions panel ▸ Load Actions…) and start the batch again.");
                        JobFinished?.Invoke(job);
                        return new BatchRunResult(BatchOutcome.AbortedMissingAction, completed, skipped, failed, ex.Message);
                    }
                    catch (OperationCanceledException)
                    {
                        job.Status = JobStatus.Queued;
                        job.Detail = null;
                        Log?.Invoke($"■ {job.DisplayName} cancelled.");
                        return new BatchRunResult(BatchOutcome.Cancelled, completed, skipped, failed);
                    }
                    catch (PhotoshopUnresponsiveException ex)
                    {
                        if (attempt > PhotoshopAutomationService.MaxRetriesPerFile)
                        {
                            job.Status = JobStatus.Failed;
                            job.Detail = $"Photoshop failed after {PhotoshopAutomationService.MaxRetriesPerFile} retries. {ex.Message}";
                            failed++;
                            Log?.Invoke($"✖ {job.DisplayName}: {job.Detail}");
                            break;
                        }

                        Log?.Invoke($"⚠ {job.DisplayName}: {ex.Message}");
                        Log?.Invoke($"  Restarting Photoshop — retry {attempt} of {PhotoshopAutomationService.MaxRetriesPerFile}.");
                        job.Detail = $"{ex.Message.TrimEnd('.')} — retry {attempt} of {PhotoshopAutomationService.MaxRetriesPerFile}…";

                        var previous = ps;
                        ps = null;
                        try
                        {
                            ps = RestartPhotoshop(previous, ct);
                        }
                        catch (OperationCanceledException)
                        {
                            job.Status = JobStatus.Queued;
                            job.Detail = null;
                            Log?.Invoke($"■ {job.DisplayName} cancelled.");
                            return new BatchRunResult(BatchOutcome.Cancelled, completed, skipped, failed);
                        }
                        catch (Exception rex)
                        {
                            job.Status = JobStatus.Failed;
                            job.Detail = $"Photoshop could not be restarted: {rex.Message}";
                            failed++;
                            Log?.Invoke($"✖ {job.DisplayName}: {job.Detail}");
                            Log?.Invoke("Stopping the batch — remaining files left queued.");
                            JobFinished?.Invoke(job);
                            return new BatchRunResult(
                                BatchOutcome.AbortedPhotoshopUnavailable, completed, skipped, failed, rex.Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        job.Status = JobStatus.Failed;
                        job.Detail = ex.Message;
                        failed++;
                        Log?.Invoke($"✖ {job.DisplayName}: {ex.Message}");
                        break;
                    }
                }

                JobFinished?.Invoke(job);
            }
        }
        finally
        {
            ps?.Dispose();
        }

        return new BatchRunResult(BatchOutcome.Completed, completed, skipped, failed);

        void Skip(BatchJob job, string reason)
        {
            job.Status = JobStatus.Skipped;
            job.Detail = reason;
            JobFinished?.Invoke(job);
            Log?.Invoke($"⏭ {job.DisplayName}: {reason}");
        }
    }

    /// <summary>Drops the dead COM server, makes sure that Photoshop.exe is gone, then
    /// starts a fresh instance. <paramref name="current"/> is disposed here.</summary>
    private PhotoshopAutomationService RestartPhotoshop(PhotoshopAutomationService current, CancellationToken ct)
    {
        var pid = current.ProcessId;
        try { current.Dispose(); }
        catch (Exception ex) { Log?.Invoke($"  (while releasing Photoshop: {ex.Message})"); }

        PhotoshopProcess.KillAndWait(pid);
        Log?.Invoke("Starting Photoshop again…");

        if (ct.CanBeCanceled)
        {
            if (ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(3)))
                throw new OperationCanceledException(ct);
        }
        else
        {
            Thread.Sleep(TimeSpan.FromSeconds(3));
        }

        var restarted = PhotoshopAutomationService.Attach();
        var pidNote = restarted.ProcessId is int id ? $" (pid {id})" : "";
        Log?.Invoke($"Reconnected to Photoshop {restarted.Version}{pidNote}.");
        if (restarted.CrashMonitorWarning is { } warning)
            Log?.Invoke(warning);
        return restarted;
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
