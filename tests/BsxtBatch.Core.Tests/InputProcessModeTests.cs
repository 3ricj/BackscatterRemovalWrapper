using BsxtBatch.Core.Models;

namespace BsxtBatch.Core.Tests;

public class InputProcessModeTests
{
    [Theory]
    [InlineData(InputProcessMode.RawsOnly, FileKind.Raw, true)]
    [InlineData(InputProcessMode.RawsOnly, FileKind.Jpeg, false)]
    [InlineData(InputProcessMode.RawsOnly, FileKind.Tiff, false)]
    [InlineData(InputProcessMode.RawsOnly, FileKind.Psd, false)]
    [InlineData(InputProcessMode.JpegsOnly, FileKind.Jpeg, true)]
    [InlineData(InputProcessMode.JpegsOnly, FileKind.Raw, false)]
    [InlineData(InputProcessMode.JpegsOnly, FileKind.Tiff, false)]
    [InlineData(InputProcessMode.Both, FileKind.Raw, true)]
    [InlineData(InputProcessMode.Both, FileKind.Jpeg, true)]
    [InlineData(InputProcessMode.Both, FileKind.Tiff, true)]
    [InlineData(InputProcessMode.Both, FileKind.Psd, true)]
    public void Allows_matches_the_process_dropdown(InputProcessMode mode, FileKind kind, bool expected)
        => Assert.Equal(expected, InputProcessModeInfo.Allows(mode, kind));

    [Theory]
    [InlineData("raw", InputProcessMode.RawsOnly)]
    [InlineData("RAWs only", InputProcessMode.RawsOnly)]
    [InlineData("jpg", InputProcessMode.JpegsOnly)]
    [InlineData("JPGs only", InputProcessMode.JpegsOnly)]
    [InlineData("both", InputProcessMode.Both)]
    public void Parse_accepts_the_dropdown_words(string text, InputProcessMode expected)
        => Assert.Equal(expected, InputProcessModeInfo.Parse(text));
}