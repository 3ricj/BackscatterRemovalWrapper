using BsxtBatch.Core;
using BsxtBatch.Core.Models;

// Headless front-end over the exact same BatchProcessor the WPF app uses.
// Validates the production pipeline against real Photoshop without a UI:
//   - clean files process end-to-end (Open -> guard -> DoAction -> SaveAs asCopy -> Close)
//   - documents that already contain BSXT/Cleanup layers are SKIPPED with a warning
//   - files already named *BSXT are skipped before Photoshop is even touched
//
// Usage: BsxtBatch.Cli [-f jpg,tif,psd] [-p raw|jpg|both] [-o <dir>] <file-or-folder> [...]
// Default export format: tif (matches the GUI default).
// -p selects inputs: raw, jpg, or both (default both — RAW, JPEG, TIFF, and PSD).
// -o redirects all outputs to one folder (default: each file's own folder).

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: BsxtBatch.Cli [-f jpg,tif,psd] [-p raw|jpg|both] <file> [<file> ...]");
    return 2;
}

var export = new ExportOptions();   // fresh instance: -o mutates OutputDirectory
var inputMode = InputProcessMode.Both;
var files = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] is "-f" or "--formats")
    {
        if (i + 1 >= args.Length)
        {
            Console.Error.WriteLine("-f requires a comma-separated list, e.g. -f tif,psd");
            return 2;
        }
        try { export = ExportOptions.FromCsv(args[++i]); }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }
    else if (args[i] is "-p" or "--process")
    {
        if (i + 1 >= args.Length)
        {
            Console.Error.WriteLine("-p requires raw, jpg, or both.");
            return 2;
        }
        try { inputMode = InputProcessModeInfo.Parse(args[++i]); }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }
    else if (args[i] is "-o" or "--output-dir")
    {
        if (i + 1 >= args.Length)
        {
            Console.Error.WriteLine("-o requires a folder path.");
            return 2;
        }
        export.OutputDirectory = Path.GetFullPath(args[++i]);
    }
    else
    {
        files.Add(args[i]);
    }
}

if (files.Count == 0)
{
    Console.Error.WriteLine("No input files given.");
    return 2;
}

var jobs = new List<BatchJob>();
foreach (var arg in files)
{
    var full = Path.GetFullPath(arg);
    if (Directory.Exists(full))
    {
        foreach (var file in SourceFileFinder.EnumerateSupportedFiles(full))
            TryAddJob(file, full);
        continue;
    }

    TryAddJob(full, null);
}

void TryAddJob(string path, string? searchRoot)
{
    var kind = FileFormatClassifier.Classify(path);
    if (!InputProcessModeInfo.Allows(inputMode, kind))
        return;

    jobs.Add(new BatchJob
    {
        SourcePath = path,
        SearchRoot = searchRoot,
        Kind = kind,
        ExpectedOutputPath = OutputNameResolver.ResolveAll(path, export).FirstOrDefault(),
    });
}

if (jobs.Count == 0)
{
    Console.Error.WriteLine("No supported images found.");
    return 2;
}


var processor = new BatchProcessor();
processor.Log += Console.WriteLine;

BatchRunResult result;
try
{
    result = await processor.RunAsync(jobs, export);
}
catch (InvalidOperationException ex)
{
    // Output-folder override rejected at start (cannot create / not writable).
    Console.Error.WriteLine($"Aborted: {ex.Message}");
    return 4;
}

Console.WriteLine();
foreach (var job in jobs)
    Console.WriteLine($"{job.Status,-10} {job.DisplayName}  {job.Detail}");

return result.Outcome switch
{
    BatchOutcome.Completed when result.Failed == 0 => 0,
    BatchOutcome.Completed => 1,
    BatchOutcome.Cancelled => 130,
    _ => 4,
};
