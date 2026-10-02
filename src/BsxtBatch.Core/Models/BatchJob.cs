using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BsxtBatch.Core.Models;

/// <summary>One file in a batch. Observable so the WPF job list updates in place.</summary>
public sealed class BatchJob : INotifyPropertyChanged
{
    private JobStatus _status = JobStatus.Queued;
    private string? _detail;
    private string? _outputPath;

    public required string SourcePath { get; init; }

    /// <summary>Folder this file was discovered under. Set for folder imports (the
    /// search includes subfolders); null when the file was added on its own.
    /// <see cref="DisplayName"/> is relative to this root.</summary>
    public string? SearchRoot { get; init; }

    public string FileName => System.IO.Path.GetFileName(SourcePath);

    /// <summary>File name, or the path relative to <see cref="SearchRoot"/> for files
    /// that came from a folder search.</summary>
    public string DisplayName
    {
        get
        {
            if (string.IsNullOrEmpty(SearchRoot))
                return FileName;

            string relative;
            try { relative = System.IO.Path.GetRelativePath(SearchRoot, SourcePath); }
            catch { return FileName; }

            // A different drive comes back fully qualified; ".." means the file
            // is not under the folder that was searched.
            if (string.IsNullOrEmpty(relative) || relative == "."
                || Path.IsPathRooted(relative) || IsOutsideRoot(relative))
                return FileName;

            return relative;
        }
    }

    private static bool IsOutsideRoot(string relative)
        => relative == ".."
           || relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal)
           || relative.StartsWith(".." + System.IO.Path.AltDirectorySeparatorChar, StringComparison.Ordinal);

    public FileKind Kind { get; init; }

    /// <summary>Expected output path, filled in by the processor before the job runs.</summary>
    public string? ExpectedOutputPath { get; set; }

    public JobStatus Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    /// <summary>Human-readable status detail (skip reason, error message, ...).</summary>
    public string? Detail
    {
        get => _detail;
        set { _detail = value; OnPropertyChanged(); }
    }

    public string? OutputPath
    {
        get => _outputPath;
        set { _outputPath = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Optional UI marshaller set by the host app (WPF). Status changes happen on
    /// the batch's worker thread; WPF bindings must be notified on the UI thread. Keeping
    /// this as a plain delegate avoids referencing WPF from the Core library.</summary>
    public static Action<Action>? UiMarshal { get; set; }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        var handler = PropertyChanged;
        if (handler is null) return;

        void Raise() => handler(this, new PropertyChangedEventArgs(name));

        if (UiMarshal is { } marshal) marshal(Raise);
        else Raise();
    }
}
