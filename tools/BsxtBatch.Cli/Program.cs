using BsxtBatch.Core;
using BsxtBatch.Core.Models;

// Headless front-end over the exact same BatchProcessor the WPF app uses.
// Validates the production pipeline against real Photoshop without a UI:
//   - clean files process end-to-end (Open -> guard -> DoAction -> SaveAs asCopy -> Close)
//   - documents that already contain BSXT/Cleanup layers are SKIPPED with a warning
//   - files already named *BSXT are skipped before Photoshop is even touched
//
// Usage: BsxtBatch.Cli [-f jpg,tif,psd] [-o <dir>] <file> [<file> ...]
// Default export format: tif (matches the GUI default).
// -o redirects all outputs to one folder (default: each file's own folder).

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: BsxtBatch.Cli [-f jpg,tif,psd] <file> [<file> ...]");
    return 2;
}

var export = new ExportOptions();   // fresh instance: -o mutates OutputDirectory
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
    jobs.Add(new BatchJob
    {
        SourcePath = full,
        Kind = FileFormatClassifier.Classify(full),
        ExpectedOutputPath = OutputNameResolver.ResolveAll(full, export).FirstOrDefault(),
    });
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
    Console.WriteLine($"{job.Status,-10} {job.FileName}  {job.Detail}");

return result.Outcome switch
{
    BatchOutcome.Completed when result.Failed == 0 => 0,
    BatchOutcome.Completed => 1,
    BatchOutcome.Cancelled => 130,
    _ => 4,
};
