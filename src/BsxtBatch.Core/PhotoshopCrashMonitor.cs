using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Management;
using System.Text.RegularExpressions;

namespace BsxtBatch.Core;

/// <summary>What Windows recorded about a Photoshop crash.</summary>
public sealed record PhotoshopCrashInfo(int ProcessId, string? FaultingModule, string? ExceptionCode)
{
    public string Describe()
    {
        var where = string.IsNullOrWhiteSpace(FaultingModule) ? "" : $" in {FaultingModule}";
        var code = string.IsNullOrWhiteSpace(ExceptionCode) ? "" : $" ({ExceptionCode})";
        return $"Photoshop crashed{where}{code}.";
    }
}

/// <summary>
/// Notices a crashed Photoshop while the Windows "has stopped working" dialog keeps
/// the dead process alive. Two independent signals: the Application Error event
/// (ID 1000) Windows writes the moment the crash happens, and the WerFault.exe
/// process that owns the dialog. Either one is enough.
/// </summary>
public sealed class PhotoshopCrashMonitor : IDisposable
{
    private const string ApplicationErrorProvider = "Application Error";
    private const int ApplicationErrorEventId = 1000;

    private static readonly TimeSpan WerFaultPollInterval = TimeSpan.FromSeconds(5);

    private readonly int? _processId;
    private readonly EventLogWatcher? _watcher;
    private readonly object _gate = new();
    private PhotoshopCrashInfo? _crash;
    private DateTime _nextWerPoll = DateTime.MinValue;
    private bool _disposed;

    /// <param name="processId">The Photoshop.exe to watch. When null, any Photoshop.exe
    /// crash counts.</param>
    public PhotoshopCrashMonitor(int? processId)
    {
        _processId = processId;

        try
        {
            var query = new EventLogQuery(
                "Application",
                PathType.LogName,
                $"*[System[Provider[@Name='{ApplicationErrorProvider}'] and (EventID={ApplicationErrorEventId})]]");
            _watcher = new EventLogWatcher(query);
            _watcher.EventRecordWritten += OnEventRecordWritten;
            _watcher.Enabled = true;
        }
        catch (Exception ex)
        {
            _watcher?.Dispose();
            _watcher = null;
            StartupWarning = $"Crash event watch unavailable ({ex.Message}); using the crash-dialog check only.";
        }
    }

    /// <summary>Set when the event-log subscription could not start.</summary>
    public string? StartupWarning { get; }

    /// <summary>Returns the crash if one has been seen. Polls for WerFault.exe at most
    /// every few seconds; the event-log signal arrives on its own.</summary>
    public bool TryGetCrash([MaybeNullWhen(false)] out PhotoshopCrashInfo crash)
    {
        lock (_gate)
        {
            if (_crash is not null)
            {
                crash = _crash;
                return true;
            }
        }

        if (DateTime.UtcNow >= _nextWerPoll)
        {
            _nextWerPoll = DateTime.UtcNow + WerFaultPollInterval;
            var targets = _processId is int pid ? new HashSet<int> { pid } : PhotoshopProcess.SnapshotIds();
            foreach (var target in targets)
            {
                if (WerFaultProcesses.FindFor(target).Count == 0)
                    continue;

                Record(new PhotoshopCrashInfo(target, null, null));
                break;
            }
        }

        lock (_gate)
        {
            crash = _crash;
            return crash is not null;
        }
    }

    private void OnEventRecordWritten(object? sender, EventRecordWrittenEventArgs e)
    {
        try
        {
            using var record = e.EventRecord;
            if (record is null)
                return;

            var values = record.Properties.Select(p => p.Value?.ToString()).ToList();
            var info = CrashEventParser.TryParse(values);
            if (info is null || !CrashEventParser.IsPhotoshop(values))
                return;
            if (_processId is int pid && info.ProcessId != pid)
                return;

            Record(info);
        }
        catch
        {
            // A malformed event must not take down the batch; WerFault polling still runs.
        }
    }

    private void Record(PhotoshopCrashInfo info)
    {
        lock (_gate)
        {
            // The event carries the module and code; prefer it over a bare WerFault sighting.
            if (_crash is null || (_crash.FaultingModule is null && info.FaultingModule is not null))
                _crash = info;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_watcher is null) return;
        try
        {
            _watcher.Enabled = false;
            _watcher.EventRecordWritten -= OnEventRecordWritten;
            _watcher.Dispose();
        }
        catch { /* shutting down */ }
    }
}

/// <summary>Reads the Application Error (1000) event fields. Windows stores them as
/// strings: [0] application name, [3] faulting module, [6] exception code (hex),
/// [8] process id (hex, no 0x prefix).</summary>
public static class CrashEventParser
{
    public static PhotoshopCrashInfo? TryParse(IReadOnlyList<string?> values)
    {
        if (values.Count < 9)
            return null;

        if (!TryParseHexPid(values[8], out var pid))
            return null;

        var module = string.IsNullOrWhiteSpace(values[3]) ? null : values[3]!.Trim();
        var code = string.IsNullOrWhiteSpace(values[6]) ? null : FormatCode(values[6]!);
        return new PhotoshopCrashInfo(pid, module, code);
    }

    public static bool IsPhotoshop(IReadOnlyList<string?> values)
        => values.Count > 0 && string.Equals(values[0]?.Trim(), "Photoshop.exe", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseHexPid(string? text, out int pid)
    {
        pid = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            s = s[2..];

        return int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out pid) && pid > 0;
    }

    private static string FormatCode(string text)
    {
        var s = text.Trim();
        return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s.ToLowerInvariant() : "0x" + s.ToLowerInvariant();
    }
}

/// <summary>Finds the WerFault.exe that shows the "has stopped working" dialog for a
/// given process. Its command line names the crashed process with <c>-p &lt;pid&gt;</c>.</summary>
public static partial class WerFaultProcesses
{
    [GeneratedRegex(@"(?:^|\s)[-/]p\s+(\d+)(?=\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex TargetPidPattern();

    public static int? TargetPid(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return null;
        var m = TargetPidPattern().Match(commandLine);
        return m.Success && int.TryParse(m.Groups[1].Value, out var pid) ? pid : null;
    }

    /// <summary>PIDs of WerFault.exe processes reporting on <paramref name="targetPid"/>.</summary>
    public static IReadOnlyList<int> FindFor(int targetPid)
    {
        var found = new List<int>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'WerFault.exe'");
            using var results = searcher.Get();
            foreach (var item in results)
            {
                using (item)
                {
                    if (TargetPid(item["CommandLine"] as string) == targetPid)
                        found.Add(Convert.ToInt32(item["ProcessId"], CultureInfo.InvariantCulture));
                }
            }
        }
        catch
        {
            // WMI unavailable: the event-log signal and the timeout still apply.
        }
        return found;
    }
}
