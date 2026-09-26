using BsxtBatch.Core;
using BsxtBatch.Core.Models;

// Headless front-end over the exact same BatchProcessor the WPF app uses.
// Validates the production pipeline against real Photoshop without a UI:
//   - clean files process end-to-end (Open -> guard -> DoAction -> SaveAs asCopy -> Close)
//   - documents that already contain BSXT/Cleanup layers are SKIPPED with a warning
//   - files already named *BSXT are skipped before Photoshop is even touched
//
// Usage: BsxtBatch.Cli <file> [<file> ...]

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: BsxtBatch.Cli <file> [<file> ...]");
    return 2;
}

var jobs = new List<BatchJob>();
foreach (var arg in args)
{
    var full = Path.GetFullPath(arg);
    jobs.Add(new BatchJob
    {
        SourcePath = full,
        Kind = FileFormatClassifier.Classify(full),
        ExpectedOutputPath = OutputNameResolver.Resolve(full),
    });
}

var processor = new BatchProcessor();
processor.Log += Console.WriteLine;

var result = await processor.RunAsync(jobs);

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
