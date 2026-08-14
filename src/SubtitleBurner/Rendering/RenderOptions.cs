namespace SubtitleBurner.Rendering;

/// <summary>
/// Rendering inputs that are not part of the visual style: target frame size
/// and host-provided hooks (keyword predicate, emoji mapping, speaker palette).
/// </summary>
/// <param name="Width">Frame width in px the overlays are laid out for (after any scaling).</param>
/// <param name="Height">Frame height in px the overlays are laid out for.</param>
/// <param name="IsKeyword">
/// Keyword highlight predicate (OpusClip-style). Only consulted when
/// <see cref="SubtitleStyle.KeywordHighlight"/> is true; null disables the feature.
/// </param>
/// <param name="KeywordEmoji">
/// Maps a keyword to an emoji drawn after it. Only consulted when
/// <see cref="SubtitleStyle.KeywordEmoji"/> is true; null/empty = no emoji.
/// </param>
/// <param name="SpeakerPalette">
/// "#RRGGBB" colors assigned to speakers by index (mod length).
/// Default: Tailwind-400 sky/amber/fuchsia/emerald.
/// </param>
public record RenderOptions(
    int Width,
    int Height,
    Func<string, bool>? IsKeyword = null,
    Func<string, string?>? KeywordEmoji = null,
    IReadOnlyList<string>? SpeakerPalette = null);
