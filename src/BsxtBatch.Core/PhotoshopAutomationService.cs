using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using BsxtBatch.Core.Models;

namespace BsxtBatch.Core;

/// <summary>Thrown when the Photoshop.Application COM ProgID cannot be instantiated.</summary>
public sealed class PhotoshopNotFoundException(string message) : Exception(message);

/// <summary>Thrown when Photoshop reports the BSXT action/set itself is missing.
/// Deliberately NOT thrown for a "command 'Make' is not available" style failure — that
/// means the action was found and started but one recorded step failed on this document.</summary>
public sealed class ActionMissingException(string message) : Exception(message);

/// <summary>Thrown (→ job Skipped) when the opened document already contains the action's
/// own output layers ("BSXT"/"Cleanup"). Re-running the action on such a document aborts
/// mid-way with "The command 'Make' is not available" — Photoshop's mask step collides
/// with layers left behind by a previous run. Per product decision we never silently
/// flatten: we skip the file with a clear warning.</summary>
public sealed class AlreadyProcessedException(string offendingLayer)
    : Exception($"Document already contains action layer '{offendingLayer}'.")
{
    public string OffendingLayer { get; } = offendingLayer;
}

/// <summary>Photoshop hung past the per-file limit, or the process/COM server died.
/// The batch kills that instance, starts a new one, and retries the file.</summary>
public sealed class PhotoshopUnresponsiveException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Drives Photoshop through late-bound COM (dynamic). Photoshop 27 (27.10.0) removed
/// action enumeration from both COM IDispatch and ExtendScript, so the action cannot be
/// preflight-checked; a missing set/action surfaces from DoAction and is reported as
/// <see cref="ActionMissingException"/>, which aborts the whole batch.
/// </summary>
public sealed class PhotoshopAutomationService : IDisposable
{
    public const string ActionName = "BSXT_ACTION (Click Here)";
    public const string ActionSetName = "BSXT";

    /// <summary>A slow image can legitimately take about 40 minutes. Past this the
    /// in-flight COM call is treated as a hang: Photoshop is killed so the call
    /// unblocks, then the file is retried.</summary>
    public static readonly TimeSpan FileAttemptTimeout = TimeSpan.FromMinutes(50);

    /// <summary>How often a still-running file reports that it is working, so a long
    /// action is not mistaken for a freeze.</summary>
    public static readonly TimeSpan ProgressPulseInterval = TimeSpan.FromMinutes(5);

    /// <summary>How many times one file may be retried after Photoshop hangs or exits.
    /// The first attempt is not a retry, so a file is processed at most 6 times.</summary>
    public const int MaxRetriesPerFile = 5;

    // Photoshop constants (PSObjectModel):
    private const int PsDisplayNoDialogs = 3;
    private const int PsDoNotSaveChanges = 2;
    private const int PsMaximumMaximize = 3; // MaximizeType.Maximum

    // Layer names the BSXT action creates. If an opened document already contains any of
    // these, it has been processed before and running the action again fails (see guard).
    private static readonly string[] ActionOutputLayerNames = ["BSXT", "Cleanup"];

    private readonly dynamic _app;
    private readonly PhotoshopCrashMonitor _crashMonitor;
    private bool _disposed;

    private PhotoshopAutomationService(dynamic app, int? processId)
    {
        _app = app;
        ProcessId = processId;
        _crashMonitor = new PhotoshopCrashMonitor(processId);
    }

    /// <summary>PID of the Photoshop.exe we attached to, when it could be identified.</summary>
    public int? ProcessId { get; }

    /// <summary>Set when crash detection is running in a reduced mode (for the batch log).</summary>
    public string? CrashMonitorWarning => _crashMonitor.StartupWarning;

    /// <summary>Attaches to a running Photoshop instance if one exists, otherwise launches
    /// one. Call from the STA thread that will drive all subsequent calls.</summary>
    public static PhotoshopAutomationService Attach()
    {
        var psType = Type.GetTypeFromProgID("Photoshop.Application")
            ?? throw new PhotoshopNotFoundException(
                "The Photoshop.Application COM ProgID was not found. Is Adobe Photoshop installed?");

        var pidsBefore = PhotoshopProcess.SnapshotIds();

        object? app;
        try
        {
            // CreateInstance returns the running instance if one is registered, else starts Photoshop.
            app = Activator.CreateInstance(psType);
        }
        catch (COMException ex)
        {
            throw new PhotoshopNotFoundException($"Could not start or attach to Photoshop: {ex.Message}");
        }

        if (app is null)
            throw new PhotoshopNotFoundException("Photoshop.Application instance is null.");

        OleMessageFilter.Register();
        var service = new PhotoshopAutomationService(app, PhotoshopProcess.Resolve(app, pidsBefore));

        // Unattended batches must never hang on a modal prompt (Camera Raw, save warnings...).
        try { service._app.DisplayDialogs = PsDisplayNoDialogs; }
        catch { /* some builds reject the setter; prompts then surface as errors instead of hangs */ }

        return service;
    }

    public string Version
    {
        get { try { return (string)_app.Version; } catch { return "unknown"; } }
    }

    /// <summary>Runs the full pipeline for one job: Open → guard → DoAction → SaveAs
    /// (asCopy) once per selected export format → Close(discard). The original file is
    /// never modified. Each selected format keeps the never-overwrite rule individually:
    /// if one selected output already exists it is skipped with the rest still written.
    /// PSD output preserves the document layers (never flattened); TIFF/JPG are flattened
    /// pixel copies as before.
    /// Returns the list of paths actually written (may be a subset of the selection).
    /// Throws <see cref="AlreadyProcessedException"/> to signal a policy skip,
    /// <see cref="ActionMissingException"/> to signal a fatal, batch-aborting condition,
    /// and <see cref="PhotoshopUnresponsiveException"/> when Photoshop hangs or dies
    /// (the caller kills it, restarts, and retries).</summary>
    /// <param name="onPulse">Optional callback while the file is still running, invoked
    /// from a watchdog thread with the elapsed time. Used to show that a long action
    /// is progress, not a freeze.</param>
    public IReadOnlyList<string> Process(
        BatchJob job,
        ExportOptions export,
        CancellationToken ct,
        Action<TimeSpan>? onPulse = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        if (!export.AnySelected)
            throw new ArgumentException("At least one export format must be selected.", nameof(export));

        // Photoshop may have crashed between files, with the dialog holding it open.
        if (_crashMonitor.TryGetCrash(out var earlier))
        {
            PhotoshopProcess.KillAndWait(ProcessId);
            throw new PhotoshopUnresponsiveException(earlier.Describe());
        }

        var signals = new AttemptSignals();
        using var done = new ManualResetEventSlim(false);
        var watcher = new Thread(() => Watch(done, ct, onPulse, signals))
        {
            IsBackground = true,
            Name = "BsxtBatch.PsWatch",
        };
        watcher.Start();

        IReadOnlyList<string>? written = null;
        Exception? error = null;
        try
        {
            written = ProcessCore(job, export, ct);
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            // Set before waking the watcher so a cancel that lands as the call returns
            // does not kill a Photoshop that already finished this file.
            signals.CallFinished = true;
            done.Set();
            try { watcher.Join(); }
            catch { /* watcher is background; a failed join must not hide the real error */ }
        }

        if (error is null && !signals.Hung && !signals.Crashed)
            return written!;

        // A finished file counts even if Cancel was clicked in the same instant.
        // A blocked call is killed by the watcher so this method can return at all.
        if (ct.IsCancellationRequested)
            throw new OperationCanceledException(ct);

        if (error is AlreadyProcessedException or ActionMissingException)
            ExceptionDispatchInfo.Capture(error).Throw();

        if (signals.CrashInfo is { } crash)
            throw new PhotoshopUnresponsiveException(crash.Describe(), error);

        if (signals.Hung)
            throw new PhotoshopUnresponsiveException(
                $"Photoshop did not finish within {(int)FileAttemptTimeout.TotalMinutes} minutes and was closed.",
                error);

        if (signals.Crashed || (error is not null && !IsTrackedProcessAlive()) ||
            (error is not null && PhotoshopComErrors.IsDisconnected(error)))
        {
            var detail = error?.Message;
            var message = string.IsNullOrWhiteSpace(detail)
                ? "Photoshop exited unexpectedly."
                : $"Photoshop stopped responding: {detail}";
            throw new PhotoshopUnresponsiveException(message, error);
        }

        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();

        throw new PhotoshopUnresponsiveException("Photoshop stopped responding.");
    }

    private IReadOnlyList<string> ProcessCore(BatchJob job, ExportOptions export, CancellationToken ct)
    {
        var planned = OutputNameResolver.ResolveAll(job.SourcePath, export);
        var existedBefore = planned.Where(File.Exists).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var written = new List<string>();
        var succeeded = false;
        dynamic? doc = null;
        var opened = false;

        try
        {
            doc = _app.Open(job.SourcePath);
            opened = true;

            // Belt and braces: the action plays against the ACTIVE document — pin it to ours.
            _app.ActiveDocument = doc;

            var offender = FindActionOutputLayer(doc);
            if (offender is not null)
                throw new AlreadyProcessedException(offender);

            try
            {
                _app.DoAction(ActionName, ActionSetName);
            }
            catch (COMException ex) when (PhotoshopComErrors.IsMissingActionMessage(ex.Message))
            {
                throw new ActionMissingException(
                    $"Photoshop could not run action '{ActionName}' (set '{ActionSetName}'): {ex.Message}");
            }

            foreach (var (format, path) in export.Selected().Zip(planned))
            {
                ct.ThrowIfCancellationRequested();

                // Never overwrite, per selected format: an existing output for one format
                // does not block writing the others.
                if (File.Exists(path))
                    continue;

                dynamic saveOptions = CreateSaveOptions(format);
                doc.SaveAs(path, saveOptions, true /* asCopy — never touch the original */);
                written.Add(path);
            }

            succeeded = true;
            return written;
        }
        finally
        {
            if (opened && doc is not null)
            {
                try { doc.Close(PsDoNotSaveChanges); }
                catch { /* best effort: never leave the doc open across files */ }
            }

            if (!succeeded)
                IncompleteOutputCleaner.DeleteUnfinished(planned, existedBefore, written);
        }
    }

    /// <summary>Runs beside the blocked COM call. An accepted DoAction does not return
    /// until Photoshop finishes, so a freeze cannot be observed on the STA thread.</summary>
    private void Watch(ManualResetEventSlim done, CancellationToken ct, Action<TimeSpan>? onPulse, AttemptSignals signals)
    {
        try
        {
            WatchLoop(done, ct, onPulse, signals);
        }
        catch
        {
            // A watcher failure must still unblock the STA thread.
            signals.Crashed = true;
            try { PhotoshopProcess.KillAndWait(ProcessId); }
            catch { /* already gone */ }
        }
    }

    private void WatchLoop(ManualResetEventSlim done, CancellationToken ct, Action<TimeSpan>? onPulse, AttemptSignals signals)
    {
        var started = Stopwatch.StartNew();
        var nextPulse = ProgressPulseInterval;

        while (true)
        {
            if (WaitForStopOrCancel(done, ct))
            {
                if (!done.IsSet && !signals.CallFinished && ct.IsCancellationRequested)
                {
                    // The STA thread sets CallFinished as soon as the COM call returns.
                    Thread.Sleep(50);
                    if (!signals.CallFinished)
                        PhotoshopProcess.KillAndWait(ProcessId);
                }
                return;
            }

            if (ProcessId is int pid && !PhotoshopProcess.IsAlive(pid))
            {
                signals.Crashed = true;
                return;
            }

            // The crash dialog keeps a dead Photoshop "alive"; the event log and the
            // dialog's WerFault.exe show the crash right away.
            if (_crashMonitor.TryGetCrash(out var crash))
            {
                signals.CrashInfo = crash;
                signals.Crashed = true;
                PhotoshopProcess.KillAndWait(ProcessId);
                return;
            }

            if (started.Elapsed >= FileAttemptTimeout)
            {
                signals.Hung = true;
                PhotoshopProcess.KillAndWait(ProcessId);
                return;
            }

            if (onPulse is not null && started.Elapsed >= nextPulse)
            {
                try { onPulse(started.Elapsed); }
                catch { /* a logging failure must not cancel the file */ }
                nextPulse += ProgressPulseInterval;
            }
        }
    }

    /// <summary>True when the attempt finished or the user cancelled. CancellationToken.None
    /// has no wait handle, so that path only watches <paramref name="done"/>.</summary>
    private static bool WaitForStopOrCancel(ManualResetEventSlim done, CancellationToken ct)
    {
        if (!ct.CanBeCanceled)
            return done.Wait(TimeSpan.FromSeconds(1));

        var index = WaitHandle.WaitAny([done.WaitHandle, ct.WaitHandle], TimeSpan.FromSeconds(1));
        return index is 0 or 1;
    }

    private bool IsTrackedProcessAlive()
        => ProcessId is not int pid || PhotoshopProcess.IsAlive(pid);

    private sealed class AttemptSignals
    {
        public volatile bool Hung;
        public volatile bool Crashed;
        public volatile bool CallFinished;
        public volatile PhotoshopCrashInfo? CrashInfo;
    }

    /// <summary>Depth-first scan for a layer named "BSXT" or "Cleanup" (the action's own
    /// output layers). Returns the first offender, or null when the document is clean.
    /// Case-insensitive; group layers are scanned recursively (bounded depth).</summary>
    private static string? FindActionOutputLayer(dynamic doc, int depth = 0)
    {
        if (depth > 8) return null;

        // foreach over the COM collection — the pattern proven reliable in the spike,
        // whereas raw index access is ambiguous (0- vs 1-based) across PS collections.
        try
        {
            foreach (dynamic layer in doc.Layers)
            {
                string name;
                try { name = (string)layer.Name; }
                catch { continue; }

                if (ActionOutputLayerNames.Any(n => name.Equals(n, StringComparison.OrdinalIgnoreCase)))
                    return name;

                // LayerSet exposes a child "Layers" collection; ArtLayer throws.
                try
                {
                    var inner = FindActionOutputLayer(layer, depth + 1);
                    if (inner is not null) return inner;
                }
                catch { /* ArtLayer — no child collection */ }
            }
        }
        catch { /* unreadable layer collection: treat as clean; DoAction reports real issues */ }

        return null;
    }

    private static dynamic CreateSaveOptions(ExportFormat format)
    {
        var progId = format switch
        {
            ExportFormat.Tiff => "Photoshop.TIFFSaveOptions",
            ExportFormat.Jpeg => "Photoshop.JPEGSaveOptions",
            ExportFormat.Psd => "Photoshop.PhotoshopSaveOptions",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown export format."),
        };
        var optsType = Type.GetTypeFromProgID(progId)
            ?? throw new InvalidOperationException($"{progId} ProgID not found.");
        dynamic opts = Activator.CreateInstance(optsType)!;

        switch (format)
        {
            case ExportFormat.Tiff:
                TrySet(() => opts.Layers = false); // flattened pixel copy
                TrySet(() => opts.EmbedColorProfile = true);
                TrySet(() => opts.Compression = 1); // TIFFEncoding.LZW
                break;

            case ExportFormat.Jpeg:
                TrySet(() => opts.Quality = 12);     // 0..12
                TrySet(() => opts.EmbedColorProfile = true);
                TrySet(() => opts.Optimized = true);
                TrySet(() => opts.MatteStyle = 2);   // MattingType.None
                break;

            case ExportFormat.Psd:
                // Keep the BSXT action's layers in the copy — that's the whole point
                // of choosing PSD. Maximize compatibility embeds a flattened composite
                // so other apps can still preview the file.
                TrySet(() => opts.Layers = true);
                TrySet(() => opts.EmbedColorProfile = true);
                TrySet(() => opts.MaximizeCompatibility = PsMaximumMaximize);
                break;
        }

        return opts;
    }

    /// <summary>Sets a COM property, tolerating builds where the property doesn't exist.</summary>
    private static void TrySet(Action setter)
    {
        try { setter(); }
        catch { /* property may not exist in this Photoshop version — ignore */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _crashMonitor.Dispose();
        OleMessageFilter.Unregister();
        try { Marshal.ReleaseComObject(_app); } catch { /* already gone */ }
    }
}
