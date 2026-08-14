using SubtitleBurner.Ffmpeg;
using Xunit;

namespace SubtitleBurner.Tests;

public class ProgressParseTests
{
    // ffmpeg -progress pipe:1 output formats across versions:
    //  out_time_ms= (microseconds, ≤7.x) — deprecated in 8
    //  out_time_us= (microseconds, 8+)  — removed_ms in 9
    //  out_time=    (HH:MM:SS.micro, all versions)

    [Fact]
    public void ParsesLegacyMicrosecondsLine()
    {
        Assert.True(SubtitleBurner.Ffmpeg.SubtitleBurner.TryParseProgress("out_time_ms=1500000", 10.0, out var v));
        Assert.Equal(0.15, v, 4);
    }

    [Fact]
    public void ParsesNewMicrosecondsLine()
    {
        Assert.True(SubtitleBurner.Ffmpeg.SubtitleBurner.TryParseProgress("out_time_us=2500000", 10.0, out var v));
        Assert.Equal(0.25, v, 4);
    }

    [Fact]
    public void ParsesClockLine()
    {
        Assert.True(SubtitleBurner.Ffmpeg.SubtitleBurner.TryParseProgress("out_time=00:00:05.500000", 10.0, out var v));
        Assert.Equal(0.55, v, 4);
    }

    [Theory]
    [InlineData("frame=123")]
    [InlineData("out_time_ms=abc")]
    [InlineData("progress=end")]
    public void IgnoresUnrelatedLines(string line)
    {
        Assert.False(SubtitleBurner.Ffmpeg.SubtitleBurner.TryParseProgress(line, 10.0, out _));
    }

    [Fact]
    public void ClampsIntoUnitInterval()
    {
        Assert.True(SubtitleBurner.Ffmpeg.SubtitleBurner.TryParseProgress("out_time_us=999000000", 10.0, out var v));
        Assert.Equal(1.0, v);
    }

    [Fact]
    public void DetectsRemovedOptionError() // ffmpeg 9 dropped -filter_complex_script
    {
        Assert.True(SubtitleBurner.Ffmpeg.SubtitleBurner.IsMissingOptionError(
            "blah\nUnrecognized option 'filter_complex_script'.\nError splitting the argument list",
            "filter_complex_script"));
        Assert.False(SubtitleBurner.Ffmpeg.SubtitleBurner.IsMissingOptionError(
            "some other failure", "filter_complex_script"));
    }
}
