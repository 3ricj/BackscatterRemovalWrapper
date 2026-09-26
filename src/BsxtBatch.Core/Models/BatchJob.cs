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

    public string FileName => System.IO.Path.GetFileName(SourcePath);

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
