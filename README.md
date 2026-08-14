# SubtitleBurner

CapCut-style burned-in subtitles for .NET. Give it word-level timestamps (from Whisper, GigaAM, YouTube captions — anything), pick a style, get back an mp4 with karaoke subtitles burned in.

```
words → SkiaSharp PNG overlays → ffmpeg filter_complex → out.mp4
```

## Features

- **Karaoke word highlighting** — boxed (CapCut-style, box jumps word to word) or pop (words appear as spoken)
- **Regular block subtitles** — wrapped lines with optional rounded box
- **6 built-in presets**: `default`, `hormozi`, `minimal`, `neon`, `capcut`, `opus` — plus per-call overrides (font, scale, colors, alignment)
- **Keyword highlight + emoji** — accent color (and emoji) on the words you choose via `Func<string, bool>`
- **Speaker colors** — configurable palette for diarized transcripts
- **Three layers, use what you need**:
  1. `SubtitleRenderer` — words → PNG frames + timings
  2. `FilterGraphBuilder` — frames → ffmpeg filter_complex string
  3. `SubtitleBurner.BurnAsync(...)` — video + words → finished mp4 in one call

## Quickstart

```csharp
using SubtitleBurner;
using SubtitleBurner.Ffmpeg;

var words = new List<SubtitleWord>
{
    new(0.00, 0.40, "Hello"),
    new(0.40, 0.80, "world"),
    new(0.80, 1.30, "from"),
    new(1.30, 2.00, "SubtitleBurner"),
};

await SubtitleBurner.Ffmpeg.SubtitleBurner.BurnAsync(
    inputVideo: "in.mp4",
    outputVideo: "out.mp4",
    words: words,
    style: new SubtitleStyle(Preset: "capcut"),
    options: new BurnOptions());   // ffmpeg found via FFMPEG_PATH env or PATH
```

## Requirements

- **.NET 10+**
- **ffmpeg + ffprobe** — pass an explicit path in `BurnOptions`, set `FFMPEG_PATH`, or have them on `PATH`
- **Linux**: `sudo apt install fonts-noto-color-emoji` for keyword emoji (rendering works without it, emoji are skipped)
- **Linux consumers**: add `SkiaSharp.NativeAssets.Linux.NoDependencies` to your app project

## Presets

| Preset | Look |
|---|---|
| `default` | White bold, black outline, bottom; yellow karaoke pop |
| `hormozi` | UPPERCASE, huge, middle-center |
| `minimal` | Smaller, thin outline, bottom |
| `neon` | White fill, thick green glow |
| `capcut` | Yellow box behind the current word, black text |
| `opus` | OpusClip: every word in a dark translucent box, current word's box turns yellow |

## License

MIT
