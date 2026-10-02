using System.Runtime.InteropServices;
using BsxtBatch.Core;
using BsxtBatch.Core.Models;

namespace BsxtBatch.Core.Tests;

public class FolderAndRecoveryTests
{
    [Fact]
    public void DisplayName_is_relative_to_the_search_root()
    {
        var job = new BatchJob
        {
            SourcePath = @"C:\dive\site\img.cr3",
            SearchRoot = @"C:\dive",
            Kind = FileKind.Raw,
        };

        Assert.Equal(@"site\img.cr3", job.DisplayName);
    }

    [Fact]
    public void DisplayName_is_the_file_name_when_there_is_no_search_root()
    {
        var job = new BatchJob
        {
            SourcePath = @"C:\dive\img.cr3",
            Kind = FileKind.Raw,
        };

        Assert.Equal("img.cr3", job.DisplayName);
    }

    [Fact]
    public void DisplayName_falls_back_when_the_file_is_outside_the_root()
    {
        var job = new BatchJob
        {
            SourcePath = @"D:\other\img.cr3",
            SearchRoot = @"C:\dive",
            Kind = FileKind.Raw,
        };

        Assert.Equal("img.cr3", job.DisplayName);
    }

    [Fact]
    public void EnumerateSupportedFiles_walks_subfolders_and_outputs_stay_beside_the_source()
    {
        var root = Path.Combine(Path.GetTempPath(), "bsxt-scan-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "site", "deep"));
            File.WriteAllText(Path.Combine(root, "top.jpg"), "x");
            File.WriteAllText(Path.Combine(root, "notes.txt"), "skip");
            File.WriteAllText(Path.Combine(root, "site", "mid.tif"), "x");
            File.WriteAllText(Path.Combine(root, "site", "deep", "raw.arw"), "x");

            var found = SourceFileFinder.EnumerateSupportedFiles(root);

            Assert.Equal(3, found.Count);
            Assert.Contains(found, p => p.EndsWith(@"site\deep\raw.arw", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(found, p => p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));

            var export = ExportOptions.DefaultTiff;
            foreach (var source in found)
            {
                var output = OutputNameResolver.ResolveAll(source, export).Single();
                Assert.Equal(Path.GetDirectoryName(source), Path.GetDirectoryName(output));
                Assert.EndsWith("-BSXT.tif", output, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DeleteUnfinished_removes_partials_and_keeps_completed_and_preexisting()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bsxt-clean-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var preexisting = Path.Combine(dir, "keep-BSXT.tif");
            var completed = Path.Combine(dir, "done-BSXT.jpg");
            var partial = Path.Combine(dir, "partial-BSXT.psd");
            File.WriteAllText(preexisting, "old");
            File.WriteAllText(completed, "ok");
            File.WriteAllText(partial, "truncated");

            var existed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { preexisting };
            IncompleteOutputCleaner.DeleteUnfinished(
                [preexisting, completed, partial],
                existed,
                [completed]);

            Assert.True(File.Exists(preexisting));
            Assert.True(File.Exists(completed));
            Assert.False(File.Exists(partial));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(PhotoshopComErrors.RpcECallRejected)]
    [InlineData(PhotoshopComErrors.RpcEServerfault)]
    [InlineData(PhotoshopComErrors.RpcSServerUnavailable)]
    [InlineData(PhotoshopComErrors.CoEObjnotconnected)]
    public void Disconnected_com_errors_are_retryable(int hresult)
        => Assert.True(PhotoshopComErrors.IsDisconnected(new COMException("server", hresult)));

    [Fact]
    public void Action_step_failures_are_not_treated_as_a_dead_photoshop()
    {
        var make = new COMException("The command \"Make\" is not currently available.", unchecked((int)0x80004005));
        Assert.False(PhotoshopComErrors.IsDisconnected(make));
    }

    [Fact]
    public void Missing_action_is_not_a_disconnect()
    {
        var missing = new COMException("The object 'action' is not currently available or cannot find the action.", unchecked((int)0x80004005));
        Assert.True(PhotoshopComErrors.IsMissingActionMessage(missing.Message));
        Assert.False(PhotoshopComErrors.IsDisconnected(missing));
    }

    [Fact]
    public void File_attempt_timeout_is_past_the_slow_processing_window()
    {
        // Slow images run up to about 40 minutes; the hang cutoff has to sit past that.
        Assert.True(PhotoshopAutomationService.FileAttemptTimeout >= TimeSpan.FromMinutes(45));
        Assert.Equal(5, PhotoshopAutomationService.MaxRetriesPerFile);
    }
}
