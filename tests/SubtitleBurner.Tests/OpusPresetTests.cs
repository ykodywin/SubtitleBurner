using SkiaSharp;
using SubtitleBurner;
using SubtitleBurner.Rendering;
using Xunit;

namespace SubtitleBurner.Tests;

public class OpusPresetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "subs-opus-" + Guid.NewGuid().ToString("N"));

    public OpusPresetTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ } }

    private static List<SubtitleWord> Words() =>
    [
        new(0.0, 0.5, "every"),
        new(0.5, 1.0, "word"),
        new(1.0, 1.5, "boxed"),
    ];

    [Fact]
    public void Preset_Opus_Exists_WithPerWordBoxes()
    {
        var p = SubtitleStyles.Resolve("opus");
        Assert.True(p.WordBoxes);
        Assert.True(p.UseBox);
    }

    [Fact]
    public void BoxedKaraoke_Opus_DrawsBoxBehindEveryWord()
    {
        var overlays = SubtitleRenderer.RenderBoxedKaraoke(
            Words(), 0.0, 1.5, new RenderOptions(640, 360), _dir, "t",
            new SubtitleStyle(Preset: "opus"));

        Assert.NotNull(overlays);
        Assert.Equal(3, overlays!.Count);

        // steady state for the last word: current word in a bright box, the two
        // previous words in dark translucent boxes — both must be present
        var last = overlays[^1].PngPath;
        using var bmp = SKBitmap.Decode(last)!;

        var bright = 0;   // current-word box (yellow)
        var darkBox = 0;  // other-word boxes (semi-dark, partial alpha)
        for (var y = 0; y < bmp.Height; y++)
        for (var x = 0; x < bmp.Width; x++)
        {
            var c = bmp.GetPixel(x, y);
            if (c.Alpha < 40) continue;
            if (c.Red > 200 && c.Green > 200 && c.Blue < 80 && c.Alpha > 200) bright++;
            else if (c.Red < 70 && c.Green < 70 && c.Blue < 70 && c.Alpha is >= 90 and <= 220) darkBox++;
        }
        Assert.True(bright > 500, $"current word box expected, got {bright} px");
        Assert.True(darkBox > 500, $"per-word boxes expected, got {darkBox} px");
    }

    [Fact]
    public void BoxedKaraoke_Capcut_KeepsSingleBoxOnly()
    {
        // regression guard: the default boxed style must NOT sprout per-word boxes.
        // Stroke antialiasing also yields semi-dark pixels, so compare against the
        // opus render of the same words: per-word boxes must dominate the count.
        var opts = new RenderOptions(640, 360);
        var capcut = SubtitleRenderer.RenderBoxedKaraoke(Words(), 0.0, 1.5, opts, _dir, "cap",
            new SubtitleStyle(Preset: "capcut"))!;
        var opus = SubtitleRenderer.RenderBoxedKaraoke(Words(), 0.0, 1.5, opts, _dir, "op",
            new SubtitleStyle(Preset: "opus"))!;

        static int DarkBoxPx(string path)
        {
            using var bmp = SKBitmap.Decode(path)!;
            var n = 0;
            for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Alpha is >= 90 and <= 220 && c.Red < 70 && c.Green < 70 && c.Blue < 70) n++;
            }
            return n;
        }

        var capcutDark = DarkBoxPx(capcut[^1].PngPath);
        var opusDark = DarkBoxPx(opus[^1].PngPath);
        Assert.True(opusDark > capcutDark * 2 + 500,
            $"opus per-word boxes must dominate: opus={opusDark}, capcut={capcutDark}");
    }
}
