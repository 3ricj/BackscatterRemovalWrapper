namespace BsxtBatch.Core.Tests;

public class CrashDetectionTests
{
    // Fields as Windows recorded the 2026-10-01 11:10 crash (Application Error, event 1000).
    private static readonly string?[] RealEvent =
    [
        "Photoshop.exe", "27.10.0.26", "6a8ca25f",
        "BackscatterXTerminator.8bf", "0.0.0.0", "6a9f845a",
        "c0000409", "00000000010ed955", "8dc8", "01dd51ce604f4bf1",
        @"C:\Program Files\Adobe\Adobe Photoshop 2026\Photoshop.exe",
        @"C:\Program Files\RC-Astro\Photoshop\BackscatterXTerminator.8bf",
        "7dbd12ef-ac76-464f-adec-b1ff214ca6bc", "", "",
    ];

    [Fact]
    public void Parses_the_real_photoshop_crash_event()
    {
        var info = CrashEventParser.TryParse(RealEvent);

        Assert.NotNull(info);
        Assert.Equal(0x8dc8, info.ProcessId);
        Assert.Equal("BackscatterXTerminator.8bf", info.FaultingModule);
        Assert.Equal("0xc0000409", info.ExceptionCode);
        Assert.True(CrashEventParser.IsPhotoshop(RealEvent));
        Assert.Equal("Photoshop crashed in BackscatterXTerminator.8bf (0xc0000409).", info.Describe());
    }

    [Fact]
    public void Accepts_a_pid_with_0x_prefix()
    {
        var values = (string?[])RealEvent.Clone();
        values[8] = "0x8DC8";

        Assert.Equal(0x8dc8, CrashEventParser.TryParse(values)!.ProcessId);
    }

    [Fact]
    public void Other_applications_are_not_photoshop()
    {
        var values = (string?[])RealEvent.Clone();
        values[0] = "notepad.exe";

        Assert.False(CrashEventParser.IsPhotoshop(values));
    }

    [Fact]
    public void Short_or_garbled_events_are_ignored()
    {
        Assert.Null(CrashEventParser.TryParse(["Photoshop.exe", "x"]));

        var values = (string?[])RealEvent.Clone();
        values[8] = "not-a-pid";
        Assert.Null(CrashEventParser.TryParse(values));
    }

    [Fact]
    public void Crash_without_details_still_describes_itself()
        => Assert.Equal("Photoshop crashed.", new PhotoshopCrashInfo(1, null, null).Describe());

    [Theory]
    [InlineData(@"C:\WINDOWS\system32\WerFault.exe -u -p 36296 -s 4120", 36296)]
    [InlineData(@"C:\WINDOWS\system32\WerFault.exe -p 38460 -ip 38460 -s 512", 38460)]
    [InlineData(@"WerFault.exe /p 1234", 1234)]
    public void Reads_the_crashed_pid_from_werfault(string commandLine, int expected)
        => Assert.Equal(expected, WerFaultProcesses.TargetPid(commandLine));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\WINDOWS\system32\WerFault.exe -u -s 4120")]
    [InlineData(@"C:\WINDOWS\system32\WerFault.exe -ip 38460")]
    public void No_pid_when_werfault_does_not_name_one(string? commandLine)
        => Assert.Null(WerFaultProcesses.TargetPid(commandLine));
}
