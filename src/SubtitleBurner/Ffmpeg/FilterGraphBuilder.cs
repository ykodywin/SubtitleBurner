using System.Globalization;
using System.Text;

namespace SubtitleBurner.Ffmpeg;

/// <summary>
/// Builds the ffmpeg filter_complex graph that overlays subtitle PNGs onto the
/// base video ([0:v]) with per-overlay enable windows. Pure string building —
/// no processes, fully unit-testable.
/// </summary>
public static class FilterGraphBuilder
{
    /// <summary>
    /// Y expression for the overlay filter. Bottom/Top are lifted/lowered by
    /// <paramref name="marginRatio"/> of the frame height (default 20% — TikTok/
    /// Shorts captions cover the lower ~15% of the frame).
    /// </summary>
    public static string YExpression(SubtitleAlign align, int frameHeight, double marginRatio = 0.20)
    {
        var marginY = ((int)Math.Round(frameHeight * marginRatio)).ToString(CultureInfo.InvariantCulture);
        return align switch
        {
            SubtitleAlign.Center => "(H-h)/2",
            SubtitleAlign.Top => marginY,
            _ => $"H-h-{marginY}",
        };
    }

    /// <summary>
    /// Full graph: chains one overlay filter per PNG (input k+1) onto [0:v],
    /// each visible only inside its [Start, End] window. eof_action=repeat holds
    /// the single-frame PNG inputs alive — do NOT pass -loop for them.
    /// </summary>
    public static string Build(
        IReadOnlyList<SubtitleOverlay> overlays, SubtitleAlign align, int frameHeight, double marginRatio = 0.20)
    {
        if (overlays.Count == 0)
            return "[0:v]null[out]";

        var y = YExpression(align, frameHeight, marginRatio);
        var sb = new StringBuilder();
        var prev = "0:v";
        for (var k = 0; k < overlays.Count; k++)
        {
            var label = k == overlays.Count - 1 ? "out" : $"ov{k}";
            var s = overlays[k].Start.ToString("F2", CultureInfo.InvariantCulture);
            var e = overlays[k].End.ToString("F2", CultureInfo.InvariantCulture);
            if (sb.Length > 0) sb.Append(';');
            sb.Append(FormattableString.Invariant(
                $"[{prev}][{k + 1}:v]overlay=x=(W-w)/2:y={y}:eof_action=repeat:enable='between(t,{s},{e})'[{label}]"));
            prev = label;
        }
        return sb.ToString();
    }
}
