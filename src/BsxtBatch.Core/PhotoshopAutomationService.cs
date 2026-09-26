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

    // Photoshop constants (PSObjectModel):
    private const int PsDisplayNoDialogs = 3;
    private const int PsDoNotSaveChanges = 2;

    // Layer names the BSXT action creates. If an opened document already contains any of
    // these, it has been processed before and running the action again fails (see guard).
    private static readonly string[] ActionOutputLayerNames = ["BSXT", "Cleanup"];

    private readonly dynamic _app;
    private bool _disposed;

    private PhotoshopAutomationService(dynamic app) => _app = app;

    /// <summary>Attaches to a running Photoshop instance if one exists, otherwise launches
    /// one. Call from the STA thread that will drive all subsequent calls.</summary>
    public static PhotoshopAutomationService Attach()
    {
        var psType = Type.GetTypeFromProgID("Photoshop.Application")
            ?? throw new PhotoshopNotFoundException(
                "The Photoshop.Application COM ProgID was not found. Is Adobe Photoshop installed?");

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
        var service = new PhotoshopAutomationService(app);

        // Unattended batches must never hang on a modal prompt (Camera Raw, save warnings...).
        try { service._app.DisplayDialogs = PsDisplayNoDialogs; }
        catch { /* some builds reject the setter; prompts then surface as errors instead of hangs */ }

        return service;
    }

    public string Version
    {
        get { try { return (string)_app.Version; } catch { return "unknown"; } }
    }

    /// <summary>Runs the full pipeline for one job: Open → guard → DoAction → SaveAs (asCopy)
    /// → Close(discard). The original file is never modified.
    /// Throws <see cref="AlreadyProcessedException"/> to signal a policy skip and
    /// <see cref="ActionMissingException"/> to signal a fatal, batch-aborting condition.</summary>
    public string Process(BatchJob job, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();

        var outputPath = OutputNameResolver.Resolve(job.SourcePath);

        dynamic doc = _app.Open(job.SourcePath);
        try
        {
            // Belt and braces: the action plays against the ACTIVE document — pin it to ours.
            _app.ActiveDocument = doc;

            var offender = FindActionOutputLayer(doc);
            if (offender is not null)
                throw new AlreadyProcessedException(offender);

            try
            {
                _app.DoAction(ActionName, ActionSetName);
            }
            catch (COMException ex) when (IsMissingActionError(ex))
            {
                throw new ActionMissingException(
                    $"Photoshop could not run action '{ActionName}' (set '{ActionSetName}'): {ex.Message}");
            }

            dynamic saveOptions = CreateSaveOptions(FileFormatClassifier.ExportsAsTiff(job.Kind));
            doc.SaveAs(outputPath, saveOptions, true /* asCopy — never touch the original */);
            return outputPath;
        }
        finally
        {
            try { doc.Close(PsDoNotSaveChanges); }
            catch { /* best effort: never leave the doc open across files */ }
        }
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

    /// <summary>Distinguishes "the action/set does not exist" from a step failure inside a
    /// found action (e.g. "command 'Make' is not available"). Only the former is fatal.</summary>
    private static bool IsMissingActionError(COMException ex)
    {
        var m = ex.Message;
        return m.Contains("action", StringComparison.OrdinalIgnoreCase) &&
               (m.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("can't find", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("cannot find", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("is not a valid", StringComparison.OrdinalIgnoreCase));
    }

    private static dynamic CreateSaveOptions(bool isTiff)
    {
        var progId = isTiff ? "Photoshop.TIFFSaveOptions" : "Photoshop.JPEGSaveOptions";
        var optsType = Type.GetTypeFromProgID(progId)
            ?? throw new InvalidOperationException($"{progId} ProgID not found.");
        dynamic opts = Activator.CreateInstance(optsType)!;

        if (isTiff)
        {
            TrySet(() => opts.Layers = false);
            TrySet(() => opts.EmbedColorProfile = true);
            TrySet(() => opts.Compression = 1); // TIFFEncoding.LZW
        }
        else
        {
            TrySet(() => opts.Quality = 12);     // 0..12
            TrySet(() => opts.EmbedColorProfile = true);
            TrySet(() => opts.Optimized = true);
            TrySet(() => opts.MatteStyle = 2);   // MattingType.None
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
        OleMessageFilter.Unregister();
        try { Marshal.ReleaseComObject(_app); } catch { /* already gone */ }
    }
}
