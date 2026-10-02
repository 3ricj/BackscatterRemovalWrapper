using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using BsxtBatch.Core;
using BsxtBatch.Core.Models;
using Microsoft.Win32;

namespace BsxtBatch.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly BatchProcessor _processor = new();
    private CancellationTokenSource? _cts;

    private bool _isRunning;
    private int _doneCount;
    private int _totalCount;

    private bool _exportJpg;
    private bool _exportTiff = true;   // default selection
    private bool _exportPsd;
    private string? _outputFolder;     // null/empty = same folder as each source

    public IReadOnlyList<InputModeOption> InputModeOptions { get; } =
    [
        new(InputProcessMode.RawsOnly, "RAWs only"),
        new(InputProcessMode.JpegsOnly, "JPGs only"),
        new(InputProcessMode.Both, "Both"),
    ];

    private InputModeOption _selectedInputMode = null!;

    /// <summary>Process dropdown. Default is Both (RAW, JPEG, TIFF, and PSD).</summary>
    public InputModeOption SelectedInputMode
    {
        get => _selectedInputMode;
        set
        {
            if (value is null || ReferenceEquals(_selectedInputMode, value))
                return;
            _selectedInputMode = value;
            OnPropertyChanged();
            ApplyTodoFilter();
            RaiseCommands();
        }
    }

    /// <summary>False while a batch is running — the Process dropdown stays put.</summary>
    public bool CanEditSettings => !IsRunning;

    public MainViewModel()
    {
        _selectedInputMode = InputModeOptions.First(o => o.Mode == InputProcessMode.Both);

        AddFilesCommand = new RelayCommand(_ => AddFilesViaDialog(), _ => !IsRunning);
        AddFolderCommand = new RelayCommand(_ => AddFolderViaDialog(), _ => !IsRunning);
        RemoveSelectedCommand = new RelayCommand(_ => RemoveSelected(), _ => !IsRunning && SelectedJob is not null);
        ClearListCommand = new RelayCommand(_ => ClearList(), _ => !IsRunning && Jobs.Count > 0);
        RunCommand = new RelayCommand(async _ => await RunAsync(), _ => !IsRunning && Jobs.Any(j => IsInTodo(j) && j.Status == JobStatus.Queued));
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsRunning);
        BrowseOutputFolderCommand = new RelayCommand(_ => BrowseOutputFolder(), _ => !IsRunning);
        ClearOutputFolderCommand = new RelayCommand(_ => ClearOutputFolder(), _ => !IsRunning && HasOutputFolderOverride);

        _processor.Log += line => OnUi(() => AppendLog(line));
        _processor.JobStarting += _ => OnUi(() => { });          // status bindings refresh themselves
        _processor.JobFinished += _ => OnUi(BumpProgress);

        ApplyTodoFilter();
    }

    /// <summary>Files the Process dropdown is currently willing to run. RAW-only and
    /// JPG-only hide the rest of the list; those files stay loaded so switching back
    /// to Both shows them again. They are never handed to the batch.</summary>
    private bool IsInTodo(BatchJob job) => InputProcessModeInfo.Allows(SelectedInputMode.Mode, job.Kind);

    private void ApplyTodoFilter()
    {
        var view = CollectionViewSource.GetDefaultView(Jobs);
        var mode = SelectedInputMode.Mode;
        if (mode == InputProcessMode.Both)
            view.Filter = null;
        else
            view.Filter = item => item is BatchJob job && InputProcessModeInfo.Allows(mode, job.Kind);
    }

    public ObservableCollection<BatchJob> Jobs { get; } = [];
    public ObservableCollection<string> LogLines { get; } = [];

    private BatchJob? _selectedJob;
    public BatchJob? SelectedJob
    {
        get => _selectedJob;
        set { _selectedJob = value; OnPropertyChanged(); }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            _isRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanEditSettings));
            RaiseCommands();
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        private set { _totalCount = value; OnPropertyChanged(); }
    }

    public int DoneCount
    {
        get => _doneCount;
        private set { _doneCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); }
    }

    public double ProgressPercent => TotalCount == 0 ? 0 : DoneCount * 100.0 / TotalCount;

    public string ProgressText => TotalCount == 0 ? "Ready" : $"{DoneCount}/{TotalCount}";

    // ── Export format options ────────────────────────────────────────────────

    /// <summary>JPG checkbox (unchecked by default).</summary>
    public bool ExportJpg
    {
        get => _exportJpg;
        set { _exportJpg = value; OnPropertyChanged(); OnExportFormatsChanged(); }
    }

    /// <summary>TIFF checkbox — the default export, checked on startup.</summary>
    public bool ExportTiff
    {
        get => _exportTiff;
        set { _exportTiff = value; OnPropertyChanged(); OnExportFormatsChanged(); }
    }

    /// <summary>PSD checkbox (unchecked by default). When selected the copy keeps layers.</summary>
    public bool ExportPsd
    {
        get => _exportPsd;
        set { _exportPsd = value; OnPropertyChanged(); OnExportFormatsChanged(); }
    }

    /// <summary>False while no format checkbox is selected — the Run button warns instead of running.</summary>
    public bool HasExportFormatSelected => ExportJpg || ExportTiff || ExportPsd;

    // ── Output folder override ───────────────────────────────────────────────

    /// <summary>Override folder for all outputs. Null/empty (default) = same folder as
    /// each source file.</summary>
    public string? OutputFolder
    {
        get => _outputFolder;
        private set
        {
            _outputFolder = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputFolderDisplay));
            OnPropertyChanged(nameof(HasOutputFolderOverride));
            OnExportFormatsChanged();   // refresh planned-output previews + command states
        }
    }

    /// <summary>Text shown in the output-folder row (read-only TextBox).</summary>
    public string OutputFolderDisplay =>
        HasOutputFolderOverride ? OutputFolder! : "(same folder as each source)";

    public bool HasOutputFolderOverride => !string.IsNullOrWhiteSpace(OutputFolder);

    private void BrowseOutputFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Select the output folder for all results" };
        if (dlg.ShowDialog() == true)
            OutputFolder = dlg.FolderName;
    }

    private void ClearOutputFolder() => OutputFolder = null;

    /// <summary>The current checkbox + output-folder state as Core-level options.</summary>
    public ExportOptions CurrentExportOptions
    {
        get
        {
            var formats = ExportFormat.None;
            if (ExportJpg) formats |= ExportFormat.Jpeg;
            if (ExportTiff) formats |= ExportFormat.Tiff;
            if (ExportPsd) formats |= ExportFormat.Psd;
            return new ExportOptions { Formats = formats, OutputDirectory = OutputFolder };
        }
    }

    private void OnExportFormatsChanged()
    {
        // Keep the job list's planned-output preview in sync and refresh validation.
        foreach (var job in Jobs)
            UpdateExpectedOutput(job);
        OnPropertyChanged(nameof(HasExportFormatSelected));
        RaiseCommands();
    }

    private void UpdateExpectedOutput(BatchJob job)
    {
        if (!HasExportFormatSelected) return;
        var planned = OutputNameResolver.ResolveAll(job.SourcePath, CurrentExportOptions);
        job.ExpectedOutputPath = planned.Count > 0 ? planned[0] : null;
    }

    public RelayCommand AddFilesCommand { get; }
    public RelayCommand AddFolderCommand { get; }
    public RelayCommand RemoveSelectedCommand { get; }
    public RelayCommand ClearListCommand { get; }
    public RelayCommand RunCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand BrowseOutputFolderCommand { get; }
    public RelayCommand ClearOutputFolderCommand { get; }

    // ── Importing ────────────────────────────────────────────────────────────

    public void AddFilesViaDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select images to process with BSXT",
            Multiselect = true,
            Filter = "Supported images|*.jpg;*.jpeg;*.tif;*.tiff;*.psd;*.psb;*.arw;*.cr2;*.cr3;*.nef;*.raf;*.orf;*.rw2;*.dng|All files|*.*",
        };
        if (dlg.ShowDialog() == true)
            AddPaths(dlg.FileNames, searchRoot: null);
    }

    public void AddFolderViaDialog()
    {
        var dlg = new OpenFolderDialog { Title = "Select a folder of images to import (subfolders are included)" };
        if (dlg.ShowDialog() == true)
            AddFolder(dlg.FolderName);
    }

    /// <summary>Drag-and-drop entry point: files pass through, folders are expanded recursively.</summary>
    public void AddDroppedPaths(IEnumerable<string> paths)
    {
        var loose = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
                AddFolder(p);
            else if (File.Exists(p))
                loose.Add(p);
        }
        if (loose.Count > 0)
            AddPaths(loose, searchRoot: null);
    }

    /// <summary>Imports every supported image under <paramref name="folder"/>, including
    /// subfolders. Outputs stay beside each source; the list shows the path relative
    /// to this folder.</summary>
    private void AddFolder(string folder)
    {
        var root = Path.GetFullPath(folder);
        try
        {
            var files = SourceFileFinder.EnumerateSupportedFiles(root);
            if (files.Count == 0)
            {
                AppendLog($"No supported images under '{root}'.");
                return;
            }

            var added = AddPaths(files, root);
            if (added == 0)
                AppendLog($"No new files under '{root}' (already in the list).");
        }
        catch (Exception ex)
        {
            AppendLog($"Could not read folder '{root}': {ex.Message}");
        }
    }

    /// <returns>How many new files were added.</returns>
    private int AddPaths(IEnumerable<string> paths, string? searchRoot = null)
    {
        var existing = Jobs.Select(j => j.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var root = string.IsNullOrWhiteSpace(searchRoot) ? null : Path.GetFullPath(searchRoot);
        int added = 0;
        int listed = 0;

        foreach (var path in paths.Select(Path.GetFullPath))
        {
            if (!existing.Add(path)) continue;

            var kind = FileFormatClassifier.Classify(path);
            if (kind == FileKind.Unsupported) continue;

            var job = new BatchJob
            {
                SourcePath = path,
                SearchRoot = root,
                Kind = kind,
                ExpectedOutputPath = HasExportFormatSelected
                    ? OutputNameResolver.ResolveAll(path, CurrentExportOptions)[0]
                    : null,
            };
            Jobs.Add(job);
            added++;
            if (IsInTodo(job))
                listed++;
        }

        if (listed > 0)
        {
            AppendLog(root is null
                ? $"Added {listed} file(s)."
                : $"Added {listed} file(s) from '{root}', including subfolders.");
        }
        else if (added > 0)
        {
            AppendLog($"No {InputProcessModeInfo.Label(SelectedInputMode.Mode)} files to show.");
        }
        RaiseCommands();
        return added;
    }

    private void RemoveSelected()
    {
        if (SelectedJob is { } job)
            Jobs.Remove(job);
        RaiseCommands();
    }

    private void ClearList()
    {
        Jobs.Clear();
        DoneCount = 0;
        TotalCount = 0;
        RaiseCommands();
    }

    // ── Execution ────────────────────────────────────────────────────────────

    private async Task RunAsync()
    {
        if (!HasExportFormatSelected)
        {
            AppendLog("⚠ Select at least one export format (JPG, TIF, or PSD) before running.");
            MessageBox.Show("Select at least one export format (JPG, TIF, or PSD).",
                "BsxtBatch", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var export = CurrentExportOptions;

        // Output-folder override: check write permission now, when Start is clicked,
        // so the user gets one clear error instead of every job failing individually.
        if (export.HasOutputOverride && !DirectoryHasWriteAccess(export.OutputDirectory!))
        {
            AppendLog($"⚠ No write permission in output folder '{export.OutputDirectory}'.");
            MessageBox.Show(
                $"No write permission in the selected output folder:\n\n{export.OutputDirectory}\n\nChoose another folder or clear the override.",
                "BsxtBatch", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var todo = Jobs.Where(IsInTodo).ToList();
        foreach (var job in todo)
        {
            job.Status = JobStatus.Queued;
            job.Detail = null;
            job.OutputPath = null;
        }

        _cts = new CancellationTokenSource();
        DoneCount = 0;
        TotalCount = todo.Count;
        IsRunning = true;
        var formatsDesc = string.Join(" + ", export.Selected().Select(ExportOptions.ExtensionFor));
        var outputDesc = export.HasOutputOverride ? export.OutputDirectory : "source folders";
        var processDesc = InputProcessModeInfo.Label(SelectedInputMode.Mode);
        AppendLog($"── Batch started (process: {processDesc}; export: {formatsDesc}; output: {outputDesc}) ──");

        try
        {
            var result = await _processor.RunAsync(todo, export, _cts.Token);
            AppendLog($"── Batch finished: {result.Completed} ok, {result.Skipped} skipped, {result.Failed} failed ({result.Outcome}). ──");
        }
        catch (Exception ex)
        {
            AppendLog($"Batch error: {ex.Message}");
            MessageBox.Show(ex.Message, "BsxtBatch", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsRunning = false;
        }
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    /// <summary>Probe-writes a temp file to test write access (same trick the processor uses).</summary>
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

    private void BumpProgress()
    {
        DoneCount = Jobs.Count(j => IsInTodo(j) && j.Status is JobStatus.Completed or JobStatus.Skipped or JobStatus.Failed);
    }

    private void AppendLog(string line)
    {
        LogLines.Add($"{DateTime.Now:HH:mm:ss}  {line}");
        if (LogLines.Count > 2000) LogLines.RemoveAt(0);
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    private void RaiseCommands()
    {
        AddFilesCommand.RaiseCanExecuteChanged();
        AddFolderCommand.RaiseCanExecuteChanged();
        RemoveSelectedCommand.RaiseCanExecuteChanged();
        ClearListCommand.RaiseCanExecuteChanged();
        RunCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        BrowseOutputFolderCommand.RaiseCanExecuteChanged();
        ClearOutputFolderCommand.RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One entry in the Process dropdown.</summary>
public sealed class InputModeOption(InputProcessMode mode, string label)
{
    public InputProcessMode Mode { get; } = mode;
    public string Label { get; } = label;
}
