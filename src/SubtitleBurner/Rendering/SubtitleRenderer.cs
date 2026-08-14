using SkiaSharp;

namespace SubtitleBurner.Rendering;

/// <summary>
/// Renders subtitle overlays with SkiaSharp — things libass can't do (rounded
/// CapCut boxes). One PNG per (line block, spoken word) state; overlay them
/// onto video with ffmpeg's overlay filter (see FilterGraphBuilder) or
/// <see cref="SubtitleBurner.Ffmpeg.SubtitleBurner"/> for a one-call burn.
/// </summary>
public static class SubtitleRenderer
{
    /// <summary>Default speaker colors (Tailwind 400): sky, amber, fuchsia, emerald.</summary>
    private static readonly string[] DefaultSpeakerPalette = ["#38bdf8", "#fbbf24", "#e879f9", "#34d399"];

    /// <summary>Default keyword accent (#FF4D4F OpusClip-ish red) when KeywordColor isn't set.</summary>
    private static readonly SKColor DefaultKeywordAccent = new(0xFF, 0x4D, 0x4F);

    /// <summary>"SPEAKER_02" -> palette color by index; null/unknown -> null.</summary>
    private static SKColor? SpeakerColor(string? speaker, SKColor[] palette)
    {
        if (string.IsNullOrEmpty(speaker)) return null;
        var idx = 0;
        var found = false;
        foreach (var ch in speaker)
        {
            if (!char.IsDigit(ch)) continue;
            idx = idx * 10 + (ch - '0');
            found = true;
        }
        return found ? palette[idx % palette.Length] : null;
    }

    /// <summary>Shared font/style setup for block rendering. Owns the typeface and font.</summary>
    internal sealed class RenderSetup : IDisposable
    {
        public required SKTypeface Typeface;
        public required SKFont Font;
        public required SKFontMetrics Metrics;
        public required string[] Disp;
        public required float[] Widths;
        public required SKColor[] Fills;
        public required float SpaceW;
        public required float LineHeight;
        public required float BoxPadX;
        public required float BoxPadY;
        public required float Radius;
        public required float OutlinePx;
        public required SKColor BoxColor;
        public required float MaxTextWidth;
        // Keyword highlight (OpusClip-style): per-word emoji + text-only widths for emoji placement
        public required float[] WordWidths;
        public required string?[] Emojis;
        public SKTypeface? EmojiTypeface;
        public SKFont? EmojiFont;
        public float EmojiGap;
        // Pop karaoke (non-box styles) and regular blocks
        public required bool[] IsKeyword;
        public required SKColor Accent;
        public required SKColor PopFill;
        public required SKColor? LineBox;
        // OpusClip-style: dark translucent rounded box behind every word (boxed karaoke)
        public required bool WordBoxes;
        public required SKColor WordBoxColor;

        public static RenderSetup? Create(
            IReadOnlyList<SubtitleWord> words, SubtitleStyle? style, RenderOptions opts)
        {
            var preset = SubtitleStyles.Resolve(style?.Preset);
            var scale = preset.FontScale * Math.Clamp(style?.FontScale ?? 1.0, 0.3, 3.0);
            var fontSize = Math.Max(28f, (float)(opts.Height * 0.033 * scale));
            var fontName = string.IsNullOrWhiteSpace(style?.FontName) ? "Arial" : style!.FontName!;
            var boxColor = ParseHexColor(style?.Color) ?? ParseAssColor(preset.KaraokePrimary) ?? SKColors.Yellow;

            var typeface = SKTypeface.FromFamilyName(fontName,
                    preset.Bold != 0 ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                    SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                ?? SKTypeface.FromFamilyName("Arial")
                ?? SKTypeface.Default;
            if (typeface is null)
                return null;

            var font = new SKFont(typeface, fontSize);
            var metrics = font.Metrics;
            const float marginX = 40f;
            var outlinePx = style?.Outline is { } ow ? Math.Clamp(ow, 0, 10) : Math.Max(2f, fontSize / 14f);

            var disp = words.Select(w => preset.Uppercase ? w.Text.ToUpperInvariant() : w.Text).ToArray();

            // Keyword highlight: accent fill for matched words; emoji appended when enabled.
            // The host supplies the predicate/emoji mapping via RenderOptions.
            var highlightOn = style?.KeywordHighlight == true && opts.IsKeyword is not null;
            var accent = ParseHexColor(style?.KeywordColor) ?? DefaultKeywordAccent;
            var isKeyword = words
                .Select(w => highlightOn && opts.IsKeyword!(w.Text))
                .ToArray();

            SKTypeface? emojiTypeface = null;
            SKFont? emojiFont = null;
            var emojis = new string?[words.Count];
            var emojiGap = fontSize * 0.15f;
            if (style?.KeywordEmoji == true && opts.KeywordEmoji is not null && isKeyword.Any(k => k))
            {
                emojiTypeface = SKTypeface.FromFamilyName("Segoe UI Emoji")   // Windows
                    ?? SKTypeface.FromFamilyName("Noto Color Emoji")          // Linux
                    ?? SKTypeface.FromFamilyName("Apple Color Emoji");        // macOS
                if (emojiTypeface is not null)
                {
                    emojiFont = new SKFont(emojiTypeface, fontSize);
                    for (var i = 0; i < words.Count; i++)
                        if (isKeyword[i] && !string.IsNullOrEmpty(opts.KeywordEmoji(words[i].Text)))
                            emojis[i] = opts.KeywordEmoji(words[i].Text);
                }
            }

            var palette = (opts.SpeakerPalette is { Count: > 0 } custom ? custom : DefaultSpeakerPalette)
                .Select(ParseHexColor)
                .Where(c => c is not null)
                .Select(c => c!.Value)
                .ToArray();
            if (palette.Length == 0)
                palette = [SKColors.White];

            var wordWidths = disp.Select(t => font.MeasureText(t)).ToArray();
            return new RenderSetup
            {
                Typeface = typeface,
                Font = font,
                Metrics = metrics,
                Disp = disp,
                // Widths include the emoji part so wrapping/boxes account for it
                Widths = wordWidths.Select((w, i) =>
                    emojis[i] is { } e ? w + emojiGap + emojiFont!.MeasureText(e) : w).ToArray(),
                // Keyword accent wins over speaker colour (the highlight is the
                // feature intent); diarized non-keyword words keep speaker colours
                Fills = words.Select((w, i) =>
                    isKeyword[i] ? accent : (SpeakerColor(w.Speaker, palette) ?? SKColors.White)).ToArray(),
                SpaceW = font.MeasureText(" "),
                LineHeight = (float)Math.Ceiling((metrics.Descent - metrics.Ascent) * 1.15),
                BoxPadX = fontSize * 0.28f,
                BoxPadY = fontSize * 0.14f,
                Radius = fontSize * 0.35f,
                OutlinePx = outlinePx,
                BoxColor = boxColor,
                MaxTextWidth = opts.Width - 2 * marginX - 2 * (fontSize * 0.28f + outlinePx),
                WordWidths = wordWidths,
                Emojis = emojis,
                EmojiTypeface = emojiTypeface,
                EmojiFont = emojiFont,
                EmojiGap = emojiGap,
                IsKeyword = isKeyword,
                Accent = accent,
                PopFill = boxColor,
                // CapCut regular subtitles: semi-opaque box behind the whole block
                // (RegularOutline carries the box colour, like ASS BorderStyle 3)
                LineBox = preset.UseBox ? ParseAssColor(preset.RegularOutline) : null,
                WordBoxes = preset.WordBoxes && preset.UseBox,
                WordBoxColor = ParseAssColor(preset.WordBoxColor) ?? new SKColor(0, 0, 0, 153),
            };
        }

        public void Dispose()
        {
            Font.Dispose();
            Typeface.Dispose();
            EmojiFont?.Dispose();
            EmojiTypeface?.Dispose();
        }
    }

    /// <summary>
    /// Greedy wrap into lines, pack lines into blocks of up to 2.
    /// A speaker change always starts a new block — one block = one utterance.
    /// </summary>
    internal static List<List<List<int>>> BuildBlocks(IReadOnlyList<SubtitleWord> words, RenderSetup st)
    {
        var blocks = new List<List<List<int>>>();
        var block = new List<List<int>>();
        var line = new List<int>();
        float lineW = 0;
        for (var i = 0; i < words.Count; i++)
        {
            if (i > 0
                && words[i].Speaker is not null && words[i - 1].Speaker is not null
                && words[i].Speaker != words[i - 1].Speaker)
            {
                if (line.Count > 0) block.Add(line);
                if (block.Count > 0) { blocks.Add(block); block = []; }
                line = [];
                lineW = 0;
            }
            var newW = line.Count == 0 ? st.Widths[i] : lineW + st.SpaceW + st.Widths[i];
            if (line.Count > 0 && newW > st.MaxTextWidth)
            {
                block.Add(line);
                if (block.Count == 2) { blocks.Add(block); block = []; }
                line = [];
                lineW = 0;
                newW = st.Widths[i];
            }
            line.Add(i);
            lineW = newW;
        }
        if (line.Count > 0) block.Add(line);
        if (block.Count > 0) blocks.Add(block);
        return blocks;
    }

    /// <summary>Pop animation: frames per word and their ease-out scales (last = steady).</summary>
    private const int PopFrames = 4;
    private static readonly float[] PopScales = [1.28f, 1.14f, 1.05f, 1.0f];
    private const double PopDuration = 0.15;

    /// <summary>Emit the overlay states for one spoken word: a single steady state,
    /// or a pop-scale animation sequence + steady state when animate is on.</summary>
    private static void EmitWordStates(
        List<SubtitleOverlay> overlays, string outputDir, string baseName,
        double s0, double e0, bool animate, Func<float, byte[]> renderAtScale)
    {
        if (!animate)
        {
            var png = Path.Combine(outputDir, $"{baseName}.png");
            File.WriteAllBytes(png, renderAtScale(1f));
            overlays.Add(new SubtitleOverlay(png, s0, e0));
            return;
        }
        // The pop fits inside the word's own window (80% of it max) — never eats
        // the next word's time
        var animDur = Math.Min(PopDuration, (e0 - s0) * 0.8);
        var frameDur = animDur / PopFrames;
        for (var f = 0; f < PopFrames; f++)
        {
            var png = Path.Combine(outputDir, $"{baseName}_p{f}.png");
            File.WriteAllBytes(png, renderAtScale(PopScales[f]));
            var fs = s0 + f * frameDur;
            overlays.Add(new SubtitleOverlay(png, fs, f + 1 < PopFrames ? fs + frameDur : s0 + animDur));
        }
        var steady = Path.Combine(outputDir, $"{baseName}.png");
        File.WriteAllBytes(steady, renderAtScale(1f));
        overlays.Add(new SubtitleOverlay(steady, s0 + animDur, e0));
    }

    /// <summary>
    /// CapCut-style: all words of the block visible, the spoken word sits in a
    /// rounded box (black text inside). Null when fonts are unavailable.
    /// Overlay timings are relative to <paramref name="start"/>.
    /// </summary>
    public static IReadOnlyList<SubtitleOverlay>? RenderBoxedKaraoke(
        IReadOnlyList<SubtitleWord> words, double start, double end,
        RenderOptions opts, string outputDir, string baseName, SubtitleStyle? style = null)
    {
        using var st = RenderSetup.Create(words, style, opts);
        if (st is null)
            return null;

        var blocks = BuildBlocks(words, st);
        Directory.CreateDirectory(outputDir);

        var overlays = new List<SubtitleOverlay>();
        for (var b = 0; b < blocks.Count; b++)
        {
            var blk = blocks[b];
            var lineWidths = blk.Select(l => l.Sum(i => st.Widths[i]) + (l.Count - 1) * st.SpaceW).ToArray();
            var wordIndices = blk.SelectMany(l => l).ToList();

            // CapCut behavior: the whole block stays visible from its first word's
            // start to its last word's end — each state lasts until the next word
            // starts (no blinking in inter-word gaps); the yellow box just jumps.
            var animate = style?.Animate == true;
            for (var w = 0; w < wordIndices.Count; w++)
            {
                var wi = wordIndices[w];
                var s0 = Math.Max(words[wi].Start, start) - start;
                var e0 = w + 1 < wordIndices.Count
                    ? Math.Max(words[wordIndices[w + 1]].Start, start) - start
                    : Math.Min(words[wi].End, end) - start;
                if (e0 - s0 < 0.04) e0 = s0 + 0.04;
                EmitWordStates(overlays, outputDir, $"{baseName}_ov{b}_{w}", s0, e0, animate,
                    scale => RenderBlockPng(st, blk, lineWidths, wi, scale));
            }
        }
        return overlays.Count == 0 ? null : overlays;
    }

    /// <summary>
    /// Pop karaoke (non-box styles): words are invisible until spoken, the spoken
    /// word pops in the preset pop colour (accent colour for keywords). One PNG per
    /// (line block, spoken word) state, like the boxed variant.
    /// </summary>
    public static IReadOnlyList<SubtitleOverlay>? RenderPopKaraoke(
        IReadOnlyList<SubtitleWord> words, double start, double end,
        RenderOptions opts, string outputDir, string baseName, SubtitleStyle? style = null)
    {
        using var st = RenderSetup.Create(words, style, opts);
        if (st is null)
            return null;

        var blocks = BuildBlocks(words, st);
        Directory.CreateDirectory(outputDir);

        var overlays = new List<SubtitleOverlay>();
        for (var b = 0; b < blocks.Count; b++)
        {
            var blk = blocks[b];
            var lineWidths = blk.Select(l => l.Sum(i => st.Widths[i]) + (l.Count - 1) * st.SpaceW).ToArray();
            var wordIndices = blk.SelectMany(l => l).ToList();

            // Same state cadence as the boxed variant: each state lasts until the
            // next word starts — the visible prefix just grows word by word
            var animate = style?.Animate == true;
            for (var w = 0; w < wordIndices.Count; w++)
            {
                var wi = wordIndices[w];
                var s0 = Math.Max(words[wi].Start, start) - start;
                var e0 = w + 1 < wordIndices.Count
                    ? Math.Max(words[wordIndices[w + 1]].Start, start) - start
                    : Math.Min(words[wi].End, end) - start;
                if (e0 - s0 < 0.04) e0 = s0 + 0.04;
                EmitWordStates(overlays, outputDir, $"{baseName}_ov{b}_{w}", s0, e0, animate,
                    scale => RenderPopBlockPng(st, blk, lineWidths, wi, scale));
            }
        }
        return overlays.Count == 0 ? null : overlays;
    }

    /// <summary>
    /// Regular (non-karaoke) subtitles from explicit lines (user-edited or transcript
    /// segments): one PNG per line — up to 2 visual lines, shown for the whole clamped
    /// range. UseBox presets (CapCut) get a semi-opaque box behind the whole block.
    /// </summary>
    public static IReadOnlyList<SubtitleOverlay>? RenderBlocks(
        IReadOnlyList<SubtitleLine> lines, double start, double end,
        RenderOptions opts, string outputDir, string baseName, SubtitleStyle? style = null)
    {
        var clamped = lines
            .Where(l => !string.IsNullOrWhiteSpace(l.Text) && l.End > start && l.Start < end)
            .Select(l => (Start: Math.Max(l.Start, start) - start, End: Math.Min(l.End, end) - start, Text: l.Text.Trim()))
            .Where(l => l.End - l.Start >= 0.05)
            .ToList();
        if (clamped.Count == 0)
            return null;

        Directory.CreateDirectory(outputDir);
        var overlays = new List<SubtitleOverlay>();
        for (var i = 0; i < clamped.Count; i++)
        {
            // Timings are irrelevant for layout — synthesize words only for wrap/measure
            var words = clamped[i].Text
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => new SubtitleWord(0, 0, w))
                .ToList();
            if (words.Count == 0) continue;
            using var st = RenderSetup.Create(words, style, opts);
            if (st is null) return null;

            // First block only — a subtitle line caps at 2 visual lines
            var blk = BuildBlocks(words, st).FirstOrDefault();
            if (blk is null || blk.Count == 0) continue;
            var lineWidths = blk.Select(l => l.Sum(w => st.Widths[w]) + (l.Count - 1) * st.SpaceW).ToArray();

            var png = Path.Combine(outputDir, $"{baseName}_ln{i}.png");
            File.WriteAllBytes(png, RenderRegularBlockPng(st, blk, lineWidths));
            overlays.Add(new SubtitleOverlay(png, clamped[i].Start, clamped[i].End));
        }
        return overlays.Count == 0 ? null : overlays;
    }

    /// <summary>
    /// Single-state preview PNG of the word-richest subtitle block — for style
    /// previews in UIs. Boxed styles show the boxed karaoke state; plain styles
    /// show the pop karaoke state (visible prefix + pop colour).
    /// </summary>
    public static byte[]? RenderPreviewPng(
        IReadOnlyList<SubtitleWord> words, RenderOptions opts, SubtitleStyle? style = null)
    {
        if (words.Count == 0)
            return null;
        using var st = RenderSetup.Create(words, style, opts);
        if (st is null)
            return null;

        // Show the word-richest block — the most representative style sample
        var block = BuildBlocks(words, st)
            .OrderByDescending(b => b.Sum(l => l.Count))
            .FirstOrDefault();
        if (block is null || block.Count == 0)
            return null;

        var lineWidths = block.Select(l => l.Sum(i => st.Widths[i]) + (l.Count - 1) * st.SpaceW).ToArray();
        var wordIndices = block.SelectMany(l => l).ToList();
        var middle = wordIndices[wordIndices.Count / 2];
        return SubtitleStyles.Resolve(style?.Preset).UseBox
            ? RenderBlockPng(st, block, lineWidths, middle)
            : RenderPopBlockPng(st, block, lineWidths, middle);
    }

    /// <summary>Non-box karaoke state: words up to <paramref name="currentWord"/> are
    /// visible (pop/accent fill + outline), future words are not drawn.
    /// <paramref name="popScale"/> scales the current word (pop animation frame).</summary>
    private static byte[] RenderPopBlockPng(
        RenderSetup st, List<List<int>> lines, float[] lineWidths, int currentWord, float popScale = 1f)
    {
        var blockW = (int)Math.Ceiling(lineWidths.Max() + 2 * (st.BoxPadX + st.OutlinePx));
        var blockH = (int)Math.Ceiling(lines.Count * st.LineHeight + 2 * (st.BoxPadY + st.OutlinePx));

        using var bitmap = new SKBitmap(blockW, blockH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        using var strokePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = st.OutlinePx * 2, Color = new SKColor(0, 0, 0, 230), StrokeJoin = SKStrokeJoin.Round };
        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        for (var l = 0; l < lines.Count; l++)
        {
            var baseline = st.OutlinePx + st.BoxPadY + l * st.LineHeight + (-st.Metrics.Ascent);
            // Center by the FULL line width — the layout stays stable while the
            // visible prefix grows word by word
            var x = (blockW - lineWidths[l]) / 2f;
            foreach (var i in lines[l])
            {
                if (i <= currentWord)
                {
                    var popping = i == currentWord && popScale != 1f;
                    if (popping)
                    {
                        canvas.Save();
                        var cx = x + st.Widths[i] / 2f;
                        var cy = baseline + (st.Metrics.Ascent + st.Metrics.Descent) / 2f;
                        canvas.Translate(cx, cy);
                        canvas.Scale(popScale);
                        canvas.Translate(-cx, -cy);
                    }
                    fillPaint.Color = st.IsKeyword[i] ? st.Accent : st.PopFill;
                    canvas.DrawText(st.Disp[i], x, baseline, SKTextAlign.Left, st.Font, strokePaint);
                    canvas.DrawText(st.Disp[i], x, baseline, SKTextAlign.Left, st.Font, fillPaint);
                    if (st.Emojis[i] is { } emoji && st.EmojiFont is not null)
                        canvas.DrawText(emoji, x + st.WordWidths[i] + st.EmojiGap, baseline, SKTextAlign.Left, st.EmojiFont, fillPaint);
                    if (popping)
                        canvas.Restore();
                }
                x += st.Widths[i] + st.SpaceW;
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Regular (non-karaoke) block: all words visible; UseBox presets get a
    /// semi-opaque rounded box behind the whole block (CapCut BorderStyle-3 look).</summary>
    private static byte[] RenderRegularBlockPng(
        RenderSetup st, List<List<int>> lines, float[] lineWidths)
    {
        var blockW = (int)Math.Ceiling(lineWidths.Max() + 2 * (st.BoxPadX + st.OutlinePx));
        var blockH = (int)Math.Ceiling(lines.Count * st.LineHeight + 2 * (st.BoxPadY + st.OutlinePx));

        using var bitmap = new SKBitmap(blockW, blockH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        // Whole-block box behind the text (CapCut regular subtitles)
        if (st.LineBox is { } lineBox)
        {
            using var lineBoxPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = lineBox };
            canvas.DrawRoundRect(SKRect.Create(0, 0, blockW, blockH), st.Radius, st.Radius, lineBoxPaint);
        }

        using var strokePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = st.OutlinePx * 2, Color = new SKColor(0, 0, 0, 230), StrokeJoin = SKStrokeJoin.Round };
        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        for (var l = 0; l < lines.Count; l++)
        {
            var baseline = st.OutlinePx + st.BoxPadY + l * st.LineHeight + (-st.Metrics.Ascent);
            var x = (blockW - lineWidths[l]) / 2f;
            foreach (var i in lines[l])
            {
                fillPaint.Color = st.Fills[i];
                canvas.DrawText(st.Disp[i], x, baseline, SKTextAlign.Left, st.Font, strokePaint);
                canvas.DrawText(st.Disp[i], x, baseline, SKTextAlign.Left, st.Font, fillPaint);
                if (st.Emojis[i] is { } emoji && st.EmojiFont is not null)
                    canvas.DrawText(emoji, x + st.WordWidths[i] + st.EmojiGap, baseline, SKTextAlign.Left, st.EmojiFont, fillPaint);
                x += st.Widths[i] + st.SpaceW;
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] RenderBlockPng(
        RenderSetup st, List<List<int>> lines, float[] lineWidths, int currentWord, float popScale = 1f)
    {
        var blockW = (int)Math.Ceiling(lineWidths.Max() + 2 * (st.BoxPadX + st.OutlinePx));
        var blockH = (int)Math.Ceiling(lines.Count * st.LineHeight + 2 * (st.BoxPadY + st.OutlinePx));

        using var bitmap = new SKBitmap(blockW, blockH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        using var strokePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = st.OutlinePx * 2, Color = new SKColor(0, 0, 0, 230), StrokeJoin = SKStrokeJoin.Round };
        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var boxedTextPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.Black };
        using var shadowPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(0, 0, 0, 120) };
        using var boxPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = st.BoxColor };
        using var wordBoxPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = st.WordBoxColor };

        for (var l = 0; l < lines.Count; l++)
        {
            var baseline = st.OutlinePx + st.BoxPadY + l * st.LineHeight + (-st.Metrics.Ascent);
            var x = (blockW - lineWidths[l]) / 2f;
            foreach (var i in lines[l])
            {
                if (i == currentWord)
                {
                    var top = baseline + st.Metrics.Ascent - st.BoxPadY;
                    var bottom = baseline + st.Metrics.Descent + st.BoxPadY;
                    var rect = new SKRect(x - st.BoxPadX, top, x + st.Widths[i] + st.BoxPadX, bottom);
                    // pop animation frame: scale box + text + emoji around the box center
                    var popping = popScale != 1f;
                    if (popping)
                    {
                        canvas.Save();
                        canvas.Translate(rect.MidX, rect.MidY);
                        canvas.Scale(popScale);
                        canvas.Translate(-rect.MidX, -rect.MidY);
                    }
                    // soft drop shadow, then the rounded box, then black text
                    canvas.DrawRoundRect(SKRect.Create(rect.Left, rect.Top + 3, rect.Width, rect.Height), st.Radius, st.Radius, shadowPaint);
                    canvas.DrawRoundRect(rect, st.Radius, st.Radius, boxPaint);
                    canvas.DrawText(st.Disp[i], x, baseline, SKTextAlign.Left, st.Font, boxedTextPaint);
                    if (st.Emojis[i] is { } boxEmoji && st.EmojiFont is not null)
                        canvas.DrawText(boxEmoji, x + st.WordWidths[i] + st.EmojiGap, baseline, SKTextAlign.Left, st.EmojiFont, fillPaint);
                    if (popping)
                        canvas.Restore();
                }
                else
                {
                    // OpusClip-style: dark translucent box behind every spoken word
                    if (st.WordBoxes)
                    {
                        var wtop = baseline + st.Metrics.Ascent - st.BoxPadY;
                        var wbottom = baseline + st.Metrics.Descent + st.BoxPadY;
                        var wrect = new SKRect(x - st.BoxPadX, wtop, x + st.Widths[i] + st.BoxPadX, wbottom);
                        canvas.DrawRoundRect(wrect, st.Radius, st.Radius, wordBoxPaint);
                    }
                    fillPaint.Color = st.Fills[i];
                    if (!st.WordBoxes)
                        canvas.DrawText(st.Disp[i], x, baseline, SKTextAlign.Left, st.Font, strokePaint);
                    canvas.DrawText(st.Disp[i], x, baseline, SKTextAlign.Left, st.Font, fillPaint);
                    // Keyword emoji follows the word
                    if (st.Emojis[i] is { } emoji && st.EmojiFont is not null)
                        canvas.DrawText(emoji, x + st.WordWidths[i] + st.EmojiGap, baseline, SKTextAlign.Left, st.EmojiFont, fillPaint);
                }
                x += st.Widths[i] + st.SpaceW;
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>"#RRGGBB" -> SKColor; null/invalid -> null.</summary>
    internal static SKColor? ParseHexColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var h = hex.Trim().TrimStart('#');
        if (h.Length != 6) return null;
        if (!int.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var rgb)) return null;
        return new SKColor((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
    }

    /// <summary>"&amp;HAABBGGRR" -> SKColor; null/invalid -> null.</summary>
    internal static SKColor? ParseAssColor(string? ass)
    {
        if (string.IsNullOrWhiteSpace(ass)) return null;
        var h = ass.Trim().TrimStart('&').TrimStart('H', 'h');
        if (h.Length != 8) return null;
        if (!long.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var v)) return null;
        var a = (byte)((v >> 24) & 0xFF);
        var b = (byte)((v >> 16) & 0xFF);
        var g = (byte)((v >> 8) & 0xFF);
        var r = (byte)(v & 0xFF);
        return new SKColor(r, g, b, (byte)(255 - a)); // ASS alpha: 0 = opaque
    }
}
