using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BsxtBatch.Core;

/// <summary>Finds and, when a batch must recover, terminates the Photoshop process
/// behind the COM server. A hung <c>DoAction</c> call never returns, so the only way
/// to unblock the STA thread is to kill that process.</summary>
internal static class PhotoshopProcess
{
    public static HashSet<int> SnapshotIds()
    {
        var ids = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName("Photoshop"))
        {
            try { ids.Add(p.Id); }
            catch { /* exited between enum and read */ }
            finally { p.Dispose(); }
        }
        return ids;
    }

    /// <summary>Best-effort PID for the instance we just attached to. Null when it
    /// cannot be pinned down (several Photoshop.exe processes and no window).</summary>
    public static int? Resolve(object app, IReadOnlySet<int> pidsBeforeLaunch)
    {
        var fromWindow = TryPidFromOleWindow(app);
        if (fromWindow is int windowPid && IsPhotoshop(windowPid))
            return windowPid;

        var now = SnapshotIds();
        var born = new List<int>();
        foreach (var id in now)
        {
            if (!pidsBeforeLaunch.Contains(id))
                born.Add(id);
        }

        if (born.Count == 1)
            return born[0];
        if (now.Count == 1)
        {
            foreach (var id in now)
                return id;
        }

        return null;
    }

    public static bool IsAlive(int processId)
    {
        try
        {
            using var p = Process.GetProcessById(processId);
            return !p.HasExited && IsPhotoshopProcessName(p.ProcessName);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Kills the tracked Photoshop, or every Photoshop.exe when the PID is
    /// unknown. Waits until the process is gone or <paramref name="wait"/> elapses.</summary>
    public static void KillAndWait(int? processId, TimeSpan? wait = null)
    {
        var budget = wait ?? TimeSpan.FromSeconds(20);
        var targets = new List<Process>();

        if (processId is int pid)
        {
            try { targets.Add(Process.GetProcessById(pid)); }
            catch (ArgumentException) { /* already gone */ }
        }
        else
        {
            targets.AddRange(Process.GetProcessesByName("Photoshop"));
        }

        // The crash dialog's WerFault.exe holds the dead Photoshop open; close it first
        // so the dialog does not linger after Photoshop is gone.
        foreach (var p in targets)
        {
            int targetId;
            try { targetId = p.Id; }
            catch { continue; }
            KillWerFaultFor(targetId);
        }

        foreach (var p in targets)
        {
            try
            {
                if (!p.HasExited)
                    p.Kill(entireProcessTree: true);
            }
            catch { /* access denied or already exiting */ }
        }

        var ms = (int)Math.Clamp(budget.TotalMilliseconds, 0, int.MaxValue);
        foreach (var p in targets)
        {
            try
            {
                if (!p.HasExited)
                    p.WaitForExit(ms);
            }
            catch { /* already gone */ }
            finally { p.Dispose(); }
        }
    }

    private static void KillWerFaultFor(int targetPid)
    {
        foreach (var werPid in WerFaultProcesses.FindFor(targetPid))
        {
            try
            {
                using var wer = Process.GetProcessById(werPid);
                wer.Kill();
                wer.WaitForExit(5000);
            }
            catch { /* already closed */ }
        }
    }

    private static bool IsPhotoshop(int processId)
    {
        try
        {
            using var p = Process.GetProcessById(processId);
            return IsPhotoshopProcessName(p.ProcessName);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPhotoshopProcessName(string name)
        => name.Equals("Photoshop", StringComparison.OrdinalIgnoreCase);

    private static int? TryPidFromOleWindow(object app)
    {
        try
        {
            var ole = (IOleWindow)app;
            ole.GetWindow(out var hwnd);
            if (hwnd == IntPtr.Zero)
                return null;
            _ = GetWindowThreadProcessId(hwnd, out var pid);
            return pid == 0 ? null : (int)pid;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}

[ComImport]
[Guid("00000114-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleWindow
{
    void GetWindow(out IntPtr phwnd);
    void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool fEnterMode);
}
