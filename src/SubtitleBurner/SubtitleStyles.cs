namespace SubtitleBurner;

/// <summary>
/// Visual preset for burned-in subtitles. Karaoke always uses a transparent
/// Secondary/Outline (words invisible until spoken); the pop colour differs per preset.
/// ASS colours are &amp;HAABBGGRR.
/// </summary>
/// <param name="Name">Preset key used by <see cref="SubtitleStyles.Resolve"/>.</param>
/// <param name="RegularPrimary">Fill colour for regular blocks, ASS &amp;HAABBGGRR.</param>
/// <param name="KaraokePrimary">Pop-in colour; box colour when <paramref name="UseBox"/>.</param>
/// <param name="RegularOutline">Outline colour; with <paramref name="UseBox"/> also the whole-line box colour.</param>
/// <param name="Bold">-1 = bold, 0 = regular weight.</param>
/// <param name="FontScale">1.0 = default size.</param>
/// <param name="Alignment">ASS numpad alignment: 2 = bottom-center, 5 = middle-center, 8 = top-center.</param>
/// <param name="Uppercase">Render all text uppercase (Hormozi-style).</param>
/// <param name="OutlineScale">1.0 = default outline; box padding when <paramref name="UseBox"/>.</param>
/// <param name="UseBox">CapCut-style: opaque box behind text — whole line (regular) or current word (karaoke).</param>
/// <param name="WordBoxes">OpusClip-style: every word in its own dark translucent box (karaoke, with <paramref name="UseBox"/>).</param>
/// <param name="WordBoxColor">ASS colour of the per-word boxes when <paramref name="WordBoxes"/> is on.</param>
public record SubtitleStylePreset(
    string Name,
    string RegularPrimary,
    string KaraokePrimary,
    string RegularOutline,
    int Bold,
    double FontScale,
    int Alignment,
    bool Uppercase,
    double OutlineScale,
    bool UseBox = false,
    bool WordBoxes = false,
    string WordBoxColor = "&H99000000");

/// <summary>
/// Built-in subtitle presets. Resolve by name via <see cref="Resolve"/>.
/// </summary>
public static class SubtitleStyles
{
    /// <summary>White bold, black outline, bottom. Karaoke: yellow pop-in.</summary>
    public static readonly SubtitleStylePreset Default = new(
        "default", "&H00FFFFFF", "&H0000FFFF", "&H00000000", -1, 1.0, 2, false, 1.0);

    /// <summary>Hormozi-style: UPPERCASE, huge, middle-center. Karaoke: yellow pop-in.</summary>
    public static readonly SubtitleStylePreset Hormozi = new(
        "hormozi", "&H00FFFFFF", "&H0000FFFF", "&H00000000", -1, 1.4, 5, true, 1.6);

    /// <summary>Minimal: smaller, not bold, thin outline, bottom. Karaoke: white pop-in.</summary>
    public static readonly SubtitleStylePreset Minimal = new(
        "minimal", "&H00FFFFFF", "&H00FFFFFF", "&H00000000", 0, 0.8, 2, false, 0.5);

    /// <summary>Neon: white fill with a thick green glow outline. Karaoke: green pop-in.</summary>
    public static readonly SubtitleStylePreset Neon = new(
        "neon", "&H00FFFFFF", "&H0000FF00", "&H0000FF00", -1, 1.1, 2, false, 1.8);

    /// <summary>
    /// CapCut: white words, the currently spoken word sits in a yellow box with
    /// black text (the box "jumps" word to word). Without karaoke: white text on a
    /// dark semi-opaque box behind the whole line.
    /// </summary>
    public static readonly SubtitleStylePreset CapCut = new(
        "capcut", "&H00FFFFFF", "&H0000FFFF", "&H96000000", -1, 1.0, 2, false, 3.0, true);

    /// <summary>
    /// OpusClip: every word sits in its own dark translucent rounded box; the
    /// currently spoken word's box turns yellow with black text.
    /// </summary>
    public static readonly SubtitleStylePreset Opus = new(
        "opus", "&H00FFFFFF", "&H0000FFFF", "&H96000000", -1, 1.0, 2, false, 3.0, true, true);

    /// <summary>All built-in preset names.</summary>
    public static readonly string[] Names = [Default.Name, Hormozi.Name, Minimal.Name, Neon.Name, CapCut.Name, Opus.Name];

    /// <summary>Resolve by name (case-insensitive); unknown/empty falls back to <see cref="Default"/>.</summary>
    public static SubtitleStylePreset Resolve(string? name) => name?.ToLowerInvariant() switch
    {
        "hormozi" => Hormozi,
        "minimal" => Minimal,
        "neon" => Neon,
        "capcut" => CapCut,
        "opus" => Opus,
        _ => Default,
    };
}
