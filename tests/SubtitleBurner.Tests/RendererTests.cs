using SkiaSharp;
using SubtitleBurner;
using SubtitleBurner.Rendering;
using Xunit;

namespace SubtitleBurner.Tests;

public class RendererTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "subs-test-" + Guid.NewGuid().ToString("N"));

    public RendererTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ } }

    private static RenderOptions Opts(int w = 640, int h = 360) => new(w, h);

    private static List<SubtitleWord> FourWords(string? speaker = null) =>
    [
        new(0.0, 0.4, "hello", speaker),
        new(0.5, 0.9, "brave", speaker),
        new(1.0, 1.4, "new", speaker),
        new(1.5, 2.0, "world", speaker),
    ];

    private static SKBitmap Decode(string path) => SKBitmap.Decode(path)
        ?? throw new InvalidOperationException($"Cannot decode {path}");

    private static int CountPixels(SKBitmap bmp, Func<SKColor, bool> predicate)
    {
        var n = 0;
        for (var y = 0; y < bmp.Height; y++)
        for (var x = 0; x < bmp.Width; x++)
        {
            var c = bmp.GetPixel(x, y);
            if (c.Alpha > 40 && predicate(c)) n++;
        }
        return n;
    }

    // ---------- boxed karaoke ----------

    [Fact]
    public void BoxedKaraoke_OneOverlayPerWord_WindowsContiguousAndClamped()
    {
        var words = FourWords();

        var overlays = SubtitleRenderer.RenderBoxedKaraoke(
            words, 0.0, 2.0, Opts(), _dir, "t", new SubtitleStyle(Preset: "capcut"));

        Assert.NotNull(overlays);
        Assert.Equal(4, overlays!.Count);
        foreach (var o in overlays) Assert.True(File.Exists(o.PngPath), o.PngPath);
        Assert.Equal(0.0, overlays[0].Start, 3);
        for (var i = 1; i < overlays.Count; i++)
            Assert.Equal(overlays[i - 1].End, overlays[i].Start, 3); // no gaps/blinking
        Assert.True(overlays[^1].End <= 2.0 + 1e-9);
    }

    [Fact]
    public void BoxedKaraoke_CurrentWordSitsInYellowBox()
    {
        var overlays = SubtitleRenderer.RenderBoxedKaraoke(
            FourWords(), 0.0, 2.0, Opts(), _dir, "t", new SubtitleStyle(Preset: "capcut"));

        using var bmp = Decode(overlays![0].PngPath);
        var yellow = CountPixels(bmp, c => c.Red > 200 && c.Green > 200 && c.Blue < 80);
        Assert.True(yellow > 200, $"expected a visible yellow box, got {yellow} yellow px");
    }

    [Fact]
    public void BoxedKaraoke_ShortWordsGetMinimumStateDuration()
    {
        var words = new List<SubtitleWord> { new(1.0, 1.01, "hi"), new(2.0, 2.5, "there") };

        var overlays = SubtitleRenderer.RenderBoxedKaraoke(
            words, 0.0, 3.0, Opts(), _dir, "t", new SubtitleStyle(Preset: "capcut"));

        Assert.NotNull(overlays);
        Assert.True(overlays![^1].End - overlays[^1].Start >= 0.04 - 1e-9);
    }

    // ---------- pop karaoke ----------

    [Fact]
    public void PopKaraoke_VisiblePrefixGrowsWordByWord()
    {
        var overlays = SubtitleRenderer.RenderPopKaraoke(
            FourWords(), 0.0, 2.0, Opts(), _dir, "t", new SubtitleStyle(Preset: "default"));

        Assert.NotNull(overlays);
        Assert.Equal(4, overlays!.Count);
        using var first = Decode(overlays[0].PngPath);
        using var last = Decode(overlays[^1].PngPath);
        var firstPx = CountPixels(first, _ => true);
        var lastPx = CountPixels(last, _ => true);
        Assert.True(firstPx > 0, "first state must show the first word");
        Assert.True(lastPx > firstPx * 2, $"visible prefix must grow: first={firstPx}, last={lastPx}");
    }

    // ---------- regular blocks ----------

    [Fact]
    public void Blocks_LinesClampedToWindow_OutsideDropped()
    {
        var lines = new List<SubtitleLine>
        {
            new(0.5, 1.5, "inside the window"),
            new(5.0, 6.0, "fully outside"),
            new(1.9, 3.0, "tail beyond window"),
        };

        var overlays = SubtitleRenderer.RenderBlocks(
            lines, 0.0, 2.0, Opts(), _dir, "t", new SubtitleStyle());

        Assert.NotNull(overlays);
        Assert.Equal(2, overlays!.Count);
        Assert.Equal(0.5, overlays[0].Start, 3);
        Assert.Equal(1.5, overlays[0].End, 3);
        Assert.Equal(1.9, overlays[1].Start, 3);
        Assert.Equal(2.0, overlays[1].End, 3); // clamped
    }

    [Fact]
    public void Blocks_BlankOrZeroLengthLinesSkipped()
    {
        var lines = new List<SubtitleLine>
        {
            new(0.0, 1.0, "   "),
            new(1.0, 1.01, "too short"),
        };

        var overlays = SubtitleRenderer.RenderBlocks(
            lines, 0.0, 2.0, Opts(), _dir, "t", new SubtitleStyle());

        Assert.Null(overlays);
    }

    // ---------- speaker / keyword behavior (regressions from production) ----------

    [Fact]
    public void BuildBlocks_SpeakerChangeFlushesBlock()
    {
        var words = new List<SubtitleWord>
        {
            new(0.0, 0.4, "first", "SPEAKER_00"),
            new(0.5, 0.9, "speaker", "SPEAKER_00"),
            new(1.0, 1.4, "second", "SPEAKER_01"),
            new(1.5, 2.0, "speaker", "SPEAKER_01"),
        };

        using var st = SubtitleRenderer.RenderSetup.Create(words, new SubtitleStyle(), Opts());
        Assert.NotNull(st);
        var blocks = SubtitleRenderer.BuildBlocks(words, st!);

        Assert.Equal(2, blocks.Count);
        Assert.Equal(2, blocks[0].SelectMany(l => l).Count());
        Assert.Equal(2, blocks[1].SelectMany(l => l).Count());
    }

    [Fact]
    public void KeywordAccent_WinsOverSpeakerColor()
    {
        // Regression: on diarized jobs every word has a speaker; if speaker fill
        // wins, the keyword highlight is silently dead.
        var words = new List<SubtitleWord>
        {
            new(0.0, 0.5, "plain", "SPEAKER_00"),
            new(0.5, 1.0, "boom", "SPEAKER_00"),
        };
        var opts = Opts() with { IsKeyword = w => w == "boom" };
        var style = new SubtitleStyle(Preset: "capcut", KeywordHighlight: true);

        var overlays = SubtitleRenderer.RenderBoxedKaraoke(words, 0.0, 1.0, opts, _dir, "t", style);

        // State 0: current word = "plain" (boxed, black text); "boom" is drawn as
        // a regular word and MUST carry the accent (#FF4D4F), not the speaker sky.
        using var bmp = Decode(overlays![0].PngPath);
        var accent = CountPixels(bmp, c => c.Red > 220 && c.Green is > 50 and < 110 && c.Blue is > 55 and < 115);
        Assert.True(accent > 100, $"keyword must render in accent color, got {accent} accent px");
    }

    [Fact]
    public void SpeakerPalette_IsConfigurable()
    {
        var words = new List<SubtitleWord> { new(0.0, 0.5, "hi", "SPEAKER_00") };
        var opts = Opts() with { SpeakerPalette = new[] { "#FF0000" } };

        var overlays = SubtitleRenderer.RenderBoxedKaraoke(
            words, 0.0, 1.0, opts, _dir, "t", new SubtitleStyle(Preset: "capcut"));

        // Boxed preview: the non-current word is drawn with its fill = speaker color
        // (pop mode paints visible words in PopFill instead — speaker colors don't apply there)
        var png = SubtitleRenderer.RenderPreviewPng(
            new List<SubtitleWord> { new(0, 0.5, "aa", "SPEAKER_00"), new(0.5, 1.0, "bb", "SPEAKER_00") },
            opts, new SubtitleStyle(Preset: "capcut"));
        Assert.NotNull(png);
        using var bmp = SKBitmap.Decode(png);
        var red = CountPixels(bmp!, c => c.Red > 200 && c.Green < 60 && c.Blue < 60);
        Assert.True(red > 50, $"custom palette must apply, got {red} red px");
        Assert.NotNull(overlays);
    }

    // ---------- preview ----------

    [Fact]
    public void Preview_EmptyWords_ReturnsNull()
    {
        Assert.Null(SubtitleRenderer.RenderPreviewPng([], Opts(), new SubtitleStyle()));
    }

    [Fact]
    public void Preview_NonEmpty_PngBytes()
    {
        var png = SubtitleRenderer.RenderPreviewPng(FourWords(), Opts(), new SubtitleStyle(Preset: "capcut"));

        Assert.NotNull(png);
        using var bmp = SKBitmap.Decode(png);
        Assert.NotNull(bmp);
    }

    // ---------- color parsing ----------

    [Theory]
    [InlineData("&H0000FFFF", 255, 255, 0, 255)]   // yellow, opaque
    [InlineData("&H96000000", 0, 0, 0, 105)]       // black, ASS alpha 0x96 -> 105
    public void ParseAssColor_RoundTrip(string ass, byte r, byte g, byte b, byte a)
    {
        var c = SubtitleRenderer.ParseAssColor(ass);
        Assert.NotNull(c);
        Assert.Equal(new SKColor(r, g, b, a), c!.Value);
    }

    [Fact]
    public void ParseHexColor_ParsesAndRejectsGarbage()
    {
        Assert.Equal(new SKColor(0xFF, 0x4D, 0x4F), SubtitleRenderer.ParseHexColor("#FF4D4F"));
        Assert.Null(SubtitleRenderer.ParseHexColor("nope"));
        Assert.Null(SubtitleRenderer.ParseHexColor(null));
    }
}
