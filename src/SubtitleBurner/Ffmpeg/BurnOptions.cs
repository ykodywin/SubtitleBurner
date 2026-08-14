namespace SubtitleBurner.Ffmpeg;

/// <summary>
/// Knobs for <see cref="SubtitleBurner.BurnAsync(string, string, IReadOnlyList{SubtitleWord}, SubtitleStyle?, BurnOptions?, IProgress{double}?, CancellationToken)"/>.
/// </summary>
/// <param name="FfmpegPath">Explicit ffmpeg path; null → FFMPEG_PATH env → PATH lookup.</param>
/// <param name="EncoderArgs">Video encoder args; default: libx264 fast crf 23.</param>
/// <param name="AudioArgs">Audio args; default: aac 192k (input without audio is tolerated).</param>
/// <param name="BottomMarginRatio">Bottom/top margin as a fraction of frame height (0.20 default).</param>
/// <param name="KaraokeWordCap">Above this many words, karaoke degrades to regular blocks (one PNG per word would explode the render).</param>
/// <param name="IsKeyword">Keyword highlight predicate (see <see cref="Rendering.RenderOptions.IsKeyword"/>).</param>
/// <param name="KeywordEmoji">Keyword → emoji mapping (see <see cref="Rendering.RenderOptions.KeywordEmoji"/>).</param>
/// <param name="SpeakerPalette">"#RRGGBB" speaker colors (see <see cref="Rendering.RenderOptions.SpeakerPalette"/>).</param>
public record BurnOptions(
    string? FfmpegPath = null,
    IReadOnlyList<string>? EncoderArgs = null,
    IReadOnlyList<string>? AudioArgs = null,
    double BottomMarginRatio = 0.20,
    int KaraokeWordCap = 300,
    Func<string, bool>? IsKeyword = null,
    Func<string, string?>? KeywordEmoji = null,
    IReadOnlyList<string>? SpeakerPalette = null);

/// <summary>Thrown when ffmpeg exits non-zero or the input can't be probed.</summary>
public sealed class SubtitleBurnerException(string message) : Exception(message);
