namespace SubtitleBurner;

/// <summary>
/// A single word with its timing on the source timeline. The unit of input
/// for karaoke rendering — obtain these from any ASR (Whisper, GigaAM, ...).
/// </summary>
/// <param name="Start">Seconds from the beginning of the source audio/video.</param>
/// <param name="End">Seconds from the beginning of the source audio/video.</param>
/// <param name="Text">The word text (punctuation attached is fine).</param>
/// <param name="Speaker">Optional speaker id from diarization (e.g. "SPEAKER_01"); drives speaker colors.</param>
public record SubtitleWord(double Start, double End, string Text, string? Speaker = null);

/// <summary>
/// A whole subtitle line without per-word timings — input for regular
/// (non-karaoke) block rendering, e.g. user-edited transcript lines.
/// </summary>
/// <param name="Start">Seconds from the beginning of the source audio/video.</param>
/// <param name="End">Seconds from the beginning of the source audio/video.</param>
/// <param name="Text">Line text; long lines are wrapped to at most two visual lines.</param>
public record SubtitleLine(double Start, double End, string Text);

/// <summary>
/// One rendered PNG frame plus the time window during which it must be visible.
/// Timings are seconds on the OUTPUT timeline (the window passed to the renderer).
/// </summary>
/// <param name="PngPath">Absolute path to the rendered transparent PNG overlay.</param>
/// <param name="Start">Show from this second.</param>
/// <param name="End">Hide at this second.</param>
public record SubtitleOverlay(string PngPath, double Start, double End);
