using SubtitleBurner;
using SubtitleBurner.Ffmpeg;
using Xunit;

namespace SubtitleBurner.Tests;

public class FilterGraphTests
{
    [Fact]
    public void YExpression_Bottom_LiftsByTwentyPercentMargin()
    {
        Assert.Equal("H-h-384", FilterGraphBuilder.YExpression(SubtitleAlign.Bottom, 1920));
        Assert.Equal("H-h-72", FilterGraphBuilder.YExpression(SubtitleAlign.Bottom, 360));
    }

    [Fact]
    public void YExpression_Center_And_Top()
    {
        Assert.Equal("(H-h)/2", FilterGraphBuilder.YExpression(SubtitleAlign.Center, 1920));
        Assert.Equal("384", FilterGraphBuilder.YExpression(SubtitleAlign.Top, 1920));
    }

    [Fact]
    public void YExpression_MarginRatioIsConfigurable()
    {
        Assert.Equal("H-h-192", FilterGraphBuilder.YExpression(SubtitleAlign.Bottom, 1920, 0.10));
    }

    [Fact]
    public void Build_TwoOverlays_ExactGraph()
    {
        var overlays = new List<SubtitleOverlay>
        {
            new("a.png", 1.0, 2.5),
            new("b.png", 2.5, 4.0),
        };

        var graph = FilterGraphBuilder.Build(overlays, SubtitleAlign.Bottom, 360);

        Assert.Equal(
            "[0:v][1:v]overlay=x=(W-w)/2:y=H-h-72:eof_action=repeat:enable='between(t,1.00,2.50)'[ov0];" +
            "[ov0][2:v]overlay=x=(W-w)/2:y=H-h-72:eof_action=repeat:enable='between(t,2.50,4.00)'[out]",
            graph);
    }

    [Fact]
    public void Build_NoOverlays_PassesThrough()
    {
        Assert.Equal("[0:v]null[out]", FilterGraphBuilder.Build([], SubtitleAlign.Bottom, 360));
    }

    [Fact]
    public void Locator_ExplicitPathWins()
    {
        Assert.Equal(@"C:\ffmpeg\bin\ffmpeg.exe", FfmpegLocator.ResolveFfmpeg(@"C:\ffmpeg\bin\ffmpeg.exe"));
    }

    [Fact]
    public void Locator_FallsBackToEnvironmentVariable()
    {
        var prev = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        Environment.SetEnvironmentVariable("FFMPEG_PATH", @"D:\tools\ffmpeg.exe");
        try
        {
            Assert.Equal(@"D:\tools\ffmpeg.exe", FfmpegLocator.ResolveFfmpeg(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FFMPEG_PATH", prev);
        }
    }

    [Fact]
    public void Locator_DefaultsToPathLookup()
    {
        var prev = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        Environment.SetEnvironmentVariable("FFMPEG_PATH", null);
        try
        {
            Assert.Equal("ffmpeg", FfmpegLocator.ResolveFfmpeg(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FFMPEG_PATH", prev);
        }
    }
}

public class BurnArgsTests
{
    [Fact]
    public void BuildArgs_UsesScriptFile_MapsGraphAndOptionalAudio()
    {
        var overlays = new List<SubtitleOverlay> { new("a.png", 0.0, 1.0) };

        var args = Ffmpeg.SubtitleBurner.BuildArgs(
            "in.mp4", overlays, "graph.txt",
            ["-c:v", "libx264"], ["-c:a", "aac"], "out.mp4");

        var joined = string.Join(" ", args);
        Assert.Contains("-filter_complex_script graph.txt", joined);
        Assert.Contains("-map [out]", joined);
        Assert.Contains("-map 0:a?", joined);
        Assert.Contains("-c:v libx264", joined);
        Assert.EndsWith("out.mp4", joined);
        // one extra input per overlay PNG
        Assert.Equal(1, args.Count(a => a == "a.png"));
    }
}
