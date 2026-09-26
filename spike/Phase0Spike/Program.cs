// Phase 0 spike — proves the Photoshop COM round-trip:
//   attach/launch -> Open -> DoAction("BSXT_ACTION (Click Here)", "BSXT")
//   -> SaveAs (asCopy: true, BSXT suffix) -> Close(false)
//
// Notes discovered on Photoshop 27 (27.10.0):
//   * ActionSet/Action objects are NOT available via COM IDispatch or ExtendScript
//     (app.actionSets is undefined, executeActionGet on the app object is unavailable),
//     so we cannot preflight-enumerate actions. A missing action surfaces from
//     DoAction itself; we treat that as fatal and give loading guidance.
//   * DisplayDialogs is set to psDisplayNoDialogs (3) so Camera Raw / save prompts
//     cannot hang an unattended batch.
//
// Usage:
//   Phase0Spike [--dry-run] [--dialogs] <file> [<file> ...]
using System.Runtime.InteropServices;

namespace Phase0Spike;

internal static class Program
{
    private const string ActionName = "BSXT_ACTION (Click Here)";
    private const string ActionSetName = "BSXT";

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var dryRun = args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
        // --dialogs: leave Photoshop dialogs ENABLED (psDisplayAllDialogs) instead of
        // the default suppression (psDisplayNoDialogs). Used to diagnose whether dialog
        // suppression changes how the BSXT action behaves under automation.
        var dialogs = args.Any(a => a.Equals("--dialogs", StringComparison.OrdinalIgnoreCase));
        var files = args
            .Where(a => !a.StartsWith("--", StringComparison.Ordinal))
            .Select(a => Path.GetFullPath(a))
            .ToArray();

        if (files.Length == 0)
        {
            Console.Error.WriteLine("Usage: Phase0Spike [--dry-run] [--dialogs] <file> [<file> ...]");
            return 2;
        }

        Console.WriteLine("[1] Locating Photoshop via COM ProgID 'Photoshop.Application' ...");
        var psType = Type.GetTypeFromProgID("Photoshop.Application");
        if (psType is null)
        {
            Console.Error.WriteLine("FATAL: Photoshop COM ProgID not found. Is Photoshop installed and registered?");
            return 3;
        }

        object? psApp;
        try
        {
            psApp = Activator.CreateInstance(psType);
        }
        catch (COMException ex)
        {
            Console.Error.WriteLine($"FATAL: Could not create Photoshop.Application instance: {ex.Message}");
            return 3;
        }

        if (psApp is null)
        {
            Console.Error.WriteLine("FATAL: Photoshop.Application instance is null.");
            return 3;
        }

        // Photoshop frequently returns SERVERCALL_RETRYLATER (RPC_E_SERVERCALL_RETRYLATER)
        // while it is mid-operation. Register the standard OLE message filter so COM calls
        // are retried transparently instead of throwing "application is busy".
        OleMessageFilter.Register();

        dynamic app = psApp;
        try
        {
            Console.WriteLine($"    Attached. Photoshop version: {app.Version}");

            if (dialogs)
            {
                Console.WriteLine("    Dialogs: left ENABLED (diagnostic --dialogs mode)");
            }
            else
            {
                // Suppress modal dialogs (psDisplayNoDialogs = 3) so the batch can never hang on a prompt.
                try { app.DisplayDialogs = 3; }
                catch (Exception ex) { Console.WriteLine($"    WARN: could not set DisplayDialogs: {ex.Message}"); }
            }

            var failures = 0;
            foreach (var file in files)
            {
                try
                {
                    ProcessFile(app, file, dryRun);
                }
                catch (ActionMissingException amEx)
                {
                    Console.Error.WriteLine($"FATAL: {amEx.Message}");
                    Console.Error.WriteLine("       Load the 'BSXT' action set in Photoshop (Window > Actions > Load Actions) and retry.");
                    return 4;
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.Error.WriteLine($"    ERROR on {Path.GetFileName(file)}: {ex.Message}");
                }
            }

            Console.WriteLine(failures == 0
                ? "[DONE] All files processed successfully."
                : $"[DONE] Completed with {failures} failure(s).");
            return failures == 0 ? 0 : 1;
        }
        catch (COMException comEx)
        {
            Console.Error.WriteLine($"COM error: {comEx.Message} (HRESULT 0x{comEx.HResult:X8})");
            return 1;
        }
        finally
        {
            OleMessageFilter.Unregister();
            Marshal.ReleaseComObject(psApp);
        }
    }

    private static void ProcessFile(dynamic app, string sourcePath, bool dryRun)
    {
        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"    SKIP (missing): {sourcePath}");
            return;
        }

        var (outputPath, isTiff) = ResolveOutput(sourcePath);
        Console.WriteLine($"[3] {Path.GetFileName(sourcePath)} -> {Path.GetFileName(outputPath)} ({(isTiff ? "TIFF" : "JPEG")})");

        if (dryRun)
        {
            Console.WriteLine("    (dry run: skipping Open/DoAction/SaveAs)");
            return;
        }

        Console.WriteLine("    Open ...");
        dynamic doc = app.Open(sourcePath);

        try
        {
            Console.WriteLine($"    DoAction('{ActionName}', '{ActionSetName}') ...");
            try
            {
                LogOpenDocuments(app);

                // The action runs against the ACTIVE document. Pin it to OUR document so a
                // stray/other open document can't change the starting context, and log the
                // active doc before and after to detect an action that switches documents
                // partway through (a recorded Open / Select step before the failing Make).
                app.ActiveDocument = doc;
                Console.WriteLine($"    [diag] expected active : '{doc.Name}'");
                Console.WriteLine($"    [diag] active before   : '{app.ActiveDocument.Name}'");

                LogDocState(doc, "before");
                LogLayerProbe(app);
                app.DoAction(ActionName, ActionSetName);
                Console.WriteLine($"    [diag] active after    : '{app.ActiveDocument.Name}'");
                LogDocState(doc, "after");
            }
            catch (COMException ex)
            {
                // A "command 'Make' is not available" error means Photoshop FOUND and STARTED
                // the action but could not execute one of its recorded steps. Do NOT report
                // that as "BSXT action set missing" — surface the real message + HRESULT, and
                // dump the history (the recorded step names show where it stopped).
                Console.Error.WriteLine(
                    $"    DoAction failed: {ex.Message} (HRESULT 0x{ex.HResult:X8})");
                LogDocState(doc, "FAILED");
                throw;
            }

            Console.WriteLine("    SaveAs (asCopy: true) ...");
            dynamic saveOptions = CreateSaveOptions(isTiff);
            doc.SaveAs(outputPath, saveOptions, true /* asCopy */);
        }
        finally
        {
            Console.WriteLine("    Close (discard the opened original, never saved) ...");
            try { doc.Close(false /* saveChanges */); } catch { /* best effort */ }
        }

        Console.WriteLine($"    OK: {outputPath}");
    }

    /// <summary>Same folder, BSXT appended before the extension. RAW and TIFF -> .tif; JPG -> .jpg.</summary>
    private static (string Path, bool IsTiff) ResolveOutput(string sourcePath)
    {
        var dir = Path.GetDirectoryName(sourcePath)!;
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();

        bool isTiff = ext is not (".jpg" or ".jpeg");
        var outExt = isTiff ? ".tif" : ".jpg";
        return (Path.Combine(dir, name + "BSXT" + outExt), isTiff);
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
            TrySet(() => opts.Quality = 12);        // 0..12
            TrySet(() => opts.EmbedColorProfile = true);
            TrySet(() => opts.Optimized = true);
            TrySet(() => opts.MatteStyle = 2);      // MattingType.None
        }
        return opts;
    }

    private static void TrySet(Action setter)
    {
        try { setter(); }
        catch { /* property may not exist in this PS version — ignore */ }
    }

    /// <summary>Diagnostic: list every document currently open in Photoshop. A stray
    /// document left over from a cancelled run changes Photoshop's state and is a
    /// prime suspect for the 'command make is not available' divergence.</summary>
    private static void LogOpenDocuments(dynamic app)
    {
        try
        {
            var names = new List<string>();
            foreach (dynamic d in app.Documents)
            {
                try { names.Add($"'{d.Name}' [{d.BitsPerChannel}-bit {d.Width}x{d.Height}]"); }
                catch { names.Add("<unreadable doc>"); }
            }
            Console.WriteLine($"    [diag] {app.Documents.Count} doc(s) open before DoAction: {string.Join(", ", names)}");
        }
        catch (Exception ex) { Console.WriteLine($"    [diag] doc-list failed: {ex.Message}"); }
    }

    /// <summary>Diagnostic: log the document state (mode/bit depth/layer count/active
    /// layer) and dump the History panel command names. The divergence between a
    /// successful run's history and a failing run's history pinpoints which action
    /// step failed (the 'make' command that raised the error).</summary>
    /// <summary>Diagnostic for the failing recorded step "Make: channel / At: mask channel /
    /// Using: reveal all" (Add layer mask). That step is illegal for some layer states, so
    /// dump the active layer and whether every layer already carries a mask. A mask that
    /// already exists (or a Background layer) is what makes 'Make' unavailable.</summary>
    private static void LogLayerProbe(dynamic app)
    {
        const string js = """
            var __out = [];
            try {
                var d = app.activeDocument;
                var hasSel = false;
                try { d.selection.bounds; hasSel = true; } catch (eSel) { hasSel = false; }
                __out.push('doc=' + d.name + ' mode=' + d.mode + ' bits=' + d.bitsPerChannel + ' sel=' + hasSel);
            } catch (e0) { __out.push('doc ERR ' + e0); }
            try {
                var al = app.activeDocument.activeLayer;
                __out.push('activeLayer=' + al.name + ' type=' + al.typename + ' kind=' + al.kind +
                           ' isBackground=' + al.isBackgroundLayer + ' allLocked=' + al.allLocked);
            } catch (e1) { __out.push('activeLayer ERR ' + e1); }
            function maskState(nm) {
                var r = new ActionReference();
                r.putProperty(stringIDToTypeID('channel'), stringIDToTypeID('mask'));
                r.putName(stringIDToTypeID('layer'), nm);
                try { executeActionGet(r); return 'HAS_MASK'; } catch (eM) { return 'none'; }
            }
            try {
                var L = app.activeDocument.layers;
                for (var i = 0; i < L.length; i++) {
                    __out.push('  L' + i + ' [' + L[i].typename + '] "' + L[i].name + '"' +
                               ' vis=' + L[i].visible + ' mask=' + maskState(L[i].name));
                }
            } catch (e2) { __out.push('layers ERR ' + e2); }
            __out.join('\n');
            """;

        try
        {
            var result = app.DoJavaScript(js) as string;
            Console.WriteLine("    [layers] " + (result ?? "<no result>")
                .Replace("\n", "\n             "));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    [layers] probe failed: {ex.Message}");
        }
    }

    private static void LogDocState(dynamic doc, string label)
    {
        try
        {
            Console.WriteLine($"    [diag] doc state ({label}): mode={doc.Mode} bits={doc.BitsPerChannel} " +
                              $"layers={doc.Layers.Count} hist={doc.HistoryStates.Count} " +
                              $"size={doc.Width}x{doc.Height} profile={doc.ColorProfileName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    [diag] could not read doc state: {ex.Message}");
        }

        try { Console.WriteLine($"    [diag] activeLayer ({label}): {doc.ActiveLayer?.Name ?? "<null>"}"); }
        catch (Exception ex) { Console.WriteLine($"    [diag] activeLayer failed: {ex.Message}"); }

        try
        {
            dynamic hist = doc.HistoryStates;
            int n = hist.Count;
            var names = new List<string>();
            for (int i = 0; i < n; i++)
            {
                try { names.Add($"{i}:{hist[i].Name}"); }
                catch { names.Add($"{i}:<err>"); }
            }
            Console.WriteLine($"    [hist:{label}] " + string.Join(" | ", names));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    [hist:{label}] could not read history: {ex.Message}");
        }
    }

    private sealed class ActionMissingException(string message) : Exception(message);
}

/// <summary>
/// Standard OLE message filter. Photoshop returns RPC_E_SERVERCALL_RETRYLATER whenever it
/// is busy; this filter makes the CLR retry the COM call instead of throwing, which is
/// essential when driving Photoshop through long-running actions.
/// </summary>
internal sealed class OleMessageFilter : IOleMessageFilter
{
    private const uint RetryDelayMs = 250;
    private const int MaxRetries = 1200; // ~5 minutes at 250ms

    private static IOleMessageFilter? _registered;

    public static void Register()
    {
        _registered = new OleMessageFilter();
        _ = CoRegisterMessageFilter(_registered, out _);
    }

    public static void Unregister()
    {
        _ = CoRegisterMessageFilter(null, out _);
        _registered = null;
    }

    int IOleMessageFilter.HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo)
        => 0; // SERVERCALL_DONE

    int IOleMessageFilter.RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType)
        => dwRejectType == 2 // SERVERCALL_RETRYLATER
            ? (int)RetryDelayMs
            : -1;            // cancel

    int IOleMessageFilter.MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType)
        => 2; // PENDINGMSG_WAITDEFPROCESS

    [DllImport("ole32.dll")]
    private static extern int CoRegisterMessageFilter(IOleMessageFilter? newFilter, out IOleMessageFilter? oldFilter);
}

// NOTE: must be at namespace scope — a nested interface cannot appear in its
// enclosing class's base list (CS0246/CS0540).
[ComVisible(true)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("00000016-0000-0000-C000-000000000046")]
internal interface IOleMessageFilter
{
    [PreserveSig] int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);

    [PreserveSig] int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType);

    [PreserveSig] int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType);
}
