using System.Diagnostics;
using SkiaSharp;
using SubtitleBurner;
using SubtitleBurner.Ffmpeg;
using Xunit;

namespace SubtitleBurner.Tests;

/// <summary>
/// End-to-end: synthetic video → BurnAsync → mp4 → frame extraction → pixel asserts.
/// Skips when no ffmpeg is available (SUBS_TEST_FFMPEG env, FFMPEG_PATH env, or PATH).
/// </summary>
public class BurnIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "subs-e2e-" + Guid.NewGuid().ToString("N"));
    private readonly string _ffmpeg;

    public BurnIntegrationTests()
    {
        Directory.CreateDirectory(_dir);
        _ffmpeg = Environment.GetEnvironmentVariable("SUBS_TEST_FFMPEG")
            ?? Environment.GetEnvironmentVariable("FFMPEG_PATH")
            ?? "ffmpeg";
    }

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ } }

    private bool FfmpegAvailable()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = _ffmpeg,
                Arguments = "-version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            p.WaitForExit(10_000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task RunFfmpegAsync(params string[] args)
    {
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = _ffmpeg,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        foreach (var a in args) p.StartInfo.ArgumentList.Add(a);
        p.Start();
        var err = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        Assert.True(p.ExitCode == 0, $"ffmpeg failed: {err[..Math.Min(err.Length, 500)]}");
    }

    private async Task<string> MakeSourceVideoAsync()
    {
        var src = Path.Combine(_dir, "in.mp4");
        await RunFfmpegAsync("-y",
            "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=30:duration=6",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=6",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", src);
        return src;
    }

    private async Task<SKBitmap> ExtractFrameAsync(string video, double atSeconds)
    {
        var png = Path.Combine(_dir, $"frame_{atSeconds}.png");
        await RunFfmpegAsync("-y", "-ss", atSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            "-i", video, "-frames:v", "1", png);
        return SKBitmap.Decode(png) ?? throw new InvalidOperationException("cannot decode frame");
    }

    private static readonly List<SubtitleWord> EightWords =
    [
        new(0.0, 0.6, "this"), new(0.6, 1.2, "is"), new(1.2, 1.8, "a"), new(1.8, 2.4, "test"),
        new(2.4, 3.0, "of"), new(3.0, 3.6, "burned"), new(3.6, 4.2, "karaoke"), new(4.2, 5.0, "subtitles"),
    ];

    [SkippableFact]
    public async Task Burn_BoxedKaraoke_ProducesVideoWithVisibleSubtitles()
    {
        Skip.IfNot(FfmpegAvailable(), "ffmpeg not available");
        var src = await MakeSourceVideoAsync();
        var outPath = Path.Combine(_dir, "out.mp4");
        var progress = new List<double>();

        var result = await SubtitleBurner.Ffmpeg.SubtitleBurner.BurnAsync(
            src, outPath, EightWords,
            new SubtitleStyle(Preset: "capcut"),
            new BurnOptions(FfmpegPath: _ffmpeg),
            new SyncProgress(progress));

        Assert.True(File.Exists(outPath));
        Assert.Equal(8, result.OverlayCount);
        Assert.Equal(320, result.Width);
        Assert.True(progress.Count > 0, "progress must be reported");
        Assert.Equal(1.0, progress[^1]);

        // Frame in the middle of the first word: yellow CapCut box in the lower band
        using var frame = await ExtractFrameAsync(outPath, 0.3);
        var yellow = 0;
        for (var y = (int)(frame.Height * 0.5); y < (int)(frame.Height * 0.95); y++)
        for (var x = 0; x < frame.Width; x++)
        {
            var c = frame.GetPixel(x, y);
            if (c.Red > 200 && c.Green > 200 && c.Blue < 80) yellow++;
        }
        Assert.True(yellow > 100, $"expected the yellow karaoke box, got {yellow} yellow px");
    }

    [SkippableFact]
    public async Task Burn_RegularLines_KeepsAudioTrack()
    {
        Skip.IfNot(FfmpegAvailable(), "ffmpeg not available");
        var src = await MakeSourceVideoAsync();
        var outPath = Path.Combine(_dir, "out_lines.mp4");
        var lines = new List<SubtitleLine>
        {
            new(0.0, 2.5, "first line of subtitles"),
            new(2.5, 5.0, "second line of subtitles"),
        };

        var result = await SubtitleBurner.Ffmpeg.SubtitleBurner.BurnAsync(
            src, outPath, lines, new SubtitleStyle(), new BurnOptions(FfmpegPath: _ffmpeg));

        Assert.Equal(2, result.OverlayCount);

        // audio survives the burn
        using var probe = Process.Start(new ProcessStartInfo
        {
            FileName = _ffmpeg,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        probe.StartInfo.ArgumentList.Add("-i");
        probe.StartInfo.ArgumentList.Add(outPath);
        probe.Start();
        var info = await probe.StandardError.ReadToEndAsync();
        await probe.WaitForExitAsync();
        Assert.Contains("Audio:", info);

        // subtitles visible mid-second-line
        using var frame = await ExtractFrameAsync(outPath, 3.5);
        var bright = 0;
        for (var y = (int)(frame.Height * 0.5); y < (int)(frame.Height * 0.95); y++)
        for (var x = 0; x < frame.Width; x++)
        {
            var c = frame.GetPixel(x, y);
            if (c.Red > 200 && c.Green > 200 && c.Blue > 200) bright++;
        }
        Assert.True(bright > 100, $"expected white subtitle text, got {bright} bright px");
    }

    [SkippableFact]
    public async Task Burn_MissingInput_Throws()
    {
        Skip.IfNot(FfmpegAvailable(), "ffmpeg not available");

        await Assert.ThrowsAsync<SubtitleBurnerException>(() =>
            SubtitleBurner.Ffmpeg.SubtitleBurner.BurnAsync(
                Path.Combine(_dir, "nope.mp4"),
                Path.Combine(_dir, "out.mp4"),
                EightWords,
                new SubtitleStyle(),
                new BurnOptions(FfmpegPath: _ffmpeg)));
    }

    // Progress<T> posts callbacks to the sync context asynchronously — flaky in
    // tests (fast runners assert before the callback lands). Synchronous instead.
    private sealed class SyncProgress(List<double> target) : IProgress<double>
    {
        public void Report(double value) => target.Add(value);
    }
}
