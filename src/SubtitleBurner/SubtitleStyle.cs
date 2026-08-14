namespace SubtitleBurner;

/// <summary>
/// Vertical placement of the subtitle block within the frame.
/// Values match ASS alignment numpad codes (2/5/8).
/// </summary>
public enum SubtitleAlign
{
    /// <summary>Bottom-center, lifted by the configured bottom margin.</summary>
    Bottom = 2,

    /// <summary>Dead center of the frame.</summary>
    Center = 5,

    /// <summary>Top-center, offset down by the margin.</summary>
    Top = 8,
}

/// <summary>
/// Per-render subtitle style: a preset name plus optional overrides
/// (null = take from the preset/defaults).
/// </summary>
/// <param name="Preset">One of <see cref="SubtitleStyles.Names"/>; unknown falls back to "default".</param>
/// <param name="FontName">Font family override (null = platform default bold sans).</param>
/// <param name="FontScale">Extra size multiplier on top of the preset (0.5–2.0 sane range).</param>
/// <param name="Color">"#RRGGBB" — fill (regular) and pop-in (karaoke) colour.</param>
/// <param name="Outline">Absolute outline width in px.</param>
/// <param name="Align">Vertical placement override.</param>
/// <param name="KeywordHighlight">OpusClip-style: words matched by the keyword predicate render in an accent colour.</param>
/// <param name="KeywordColor">"#RRGGBB" — accent colour (default #FF4D4F).</param>
/// <param name="KeywordEmoji">Draw a category emoji after highlighted keywords.</param>
/// <param name="Animate">
/// Smooth pop: the newly spoken word scales 1.28→1.0 over ~150ms (rendered as
/// extra PNG states). Multiplies the overlay count — mind the karaoke word cap.
/// </param>
public record SubtitleStyle(
    string Preset = "default",
    string? FontName = null,
    double? FontScale = null,
    string? Color = null,
    int? Outline = null,
    SubtitleAlign? Align = null,
    bool? KeywordHighlight = null,
    string? KeywordColor = null,
    bool? KeywordEmoji = null,
    bool? Animate = null);
