using SkiaSharp;
using SubtitleBurner;
using SubtitleBurner.Rendering;
using Xunit;

namespace SubtitleBurner.Tests;

public class RendererAnimationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "subs-anim-" + Guid.NewGuid().ToString("N"));

    public RendererAnimationTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ } }

    private static RenderOptions Opts() => new(640, 360);

    private static List<SubtitleWord> TwoWords() =>
    [
        new(0.0, 1.0, "hello"),
        new(1.0, 2.0, "world"),
    ];

    private static int CountPixels(string path, Func<SKColor, bool> predicate)
    {
        using var bmp = SKBitmap.Decode(path)!;
        var n = 0;
        for (var y = 0; y < bmp.Height; y++)
        for (var x = 0; x < bmp.Width; x++)
        {
            var c = bmp.GetPixel(x, y);
            if (c.Alpha > 40 && predicate(c)) n++;
        }
        return n;
    }

    private static bool IsYellow(SKColor c) => c.Red > 200 && c.Green > 200 && c.Blue < 80;

    [Fact]
    public void BoxedKaraoke_Animate_EmitsMultipleFramesPerWord()
    {
        var overlays = SubtitleRenderer.RenderBoxedKaraoke(
            TwoWords(), 0.0, 2.0, Opts(), _dir, "t",
            new SubtitleStyle(Preset: "capcut", Animate: true));

        Assert.NotNull(overlays);
        // more states than words (pop frames + steady state)
        Assert.True(overlays!.Count > 2, $"expected pop frames, got {overlays.Count} overlays");
        // windows still contiguous, no gaps/blinking
        for (var i = 1; i < overlays.Count; i++)
            Assert.Equal(overlays[i - 1].End, overlays[i].Start, 3);
        Assert.Equal(0.0, overlays[0].Start, 3);
        // the pop animation fits inside the word's own window: word 2 pops at t=1.0
        var word2States = overlays.Where(o => o.Start >= 1.0 - 1e-6).ToList();
        Assert.True(word2States.Count > 1, "word 2 must have pop frames");
        Assert.Equal(1.0, word2States[0].Start, 3);
    }

    [Fact]
    public void BoxedKaraoke_Animate_BoxShrinksAcrossPopFrames()
    {
        var overlays = SubtitleRenderer.RenderBoxedKaraoke(
            TwoWords(), 0.0, 2.0, Opts(), _dir, "t",
            new SubtitleStyle(Preset: "capcut", Animate: true))!;

        // word 2 states: first pop frame (scaled up) vs steady state (scale 1.0)
        var word2 = overlays.Where(o => o.Start >= 1.0 - 1e-6).OrderBy(o => o.Start).ToList();
        var firstYellow = CountPixels(word2.First().PngPath, IsYellow);
        var steadyYellow = CountPixels(word2.Last().PngPath, IsYellow);
        Assert.True(firstYellow > steadyYellow * 1.15,
            $"pop frame box must be bigger: first={firstYellow}, steady={steadyYellow}");
    }

    [Fact]
    public void BoxedKaraoke_NoAnimate_KeepsOneStatePerWord()
    {
        var overlays = SubtitleRenderer.RenderBoxedKaraoke(
            TwoWords(), 0.0, 2.0, Opts(), _dir, "t", new SubtitleStyle(Preset: "capcut"));

        Assert.NotNull(overlays);
        Assert.Equal(2, overlays!.Count);
    }

    [Fact]
    public void PopKaraoke_Animate_NewWordPopsInScaled()
    {
        var overlays = SubtitleRenderer.RenderPopKaraoke(
            TwoWords(), 0.0, 2.0, Opts(), _dir, "t",
            new SubtitleStyle(Preset: "default", Animate: true))!;

        Assert.NotNull(overlays);
        var word2 = overlays.Where(o => o.Start >= 1.0 - 1e-6).OrderBy(o => o.Start).ToList();
        Assert.True(word2.Count > 1, "word 2 must have pop frames");
        // visible ink (any non-transparent pixel) shrinks as the pop settles
        var firstInk = CountPixels(word2.First().PngPath, _ => true);
        var steadyInk = CountPixels(word2.Last().PngPath, _ => true);
        Assert.True(firstInk > steadyInk * 1.05,
            $"pop frame must have more ink: first={firstInk}, steady={steadyInk}");
    }
}
