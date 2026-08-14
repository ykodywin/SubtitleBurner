namespace SubtitleBurner.Ffmpeg;

/// <summary>
/// Locates the ffmpeg binary: explicit path → FFMPEG_PATH environment variable
/// → bare "ffmpeg" (resolved via PATH by the OS).
/// </summary>
public static class FfmpegLocator
{
    /// <summary>Resolve the ffmpeg executable path/name.</summary>
    public static string ResolveFfmpeg(string? explicitPath = null)
        => !string.IsNullOrWhiteSpace(explicitPath) ? explicitPath
            : Environment.GetEnvironmentVariable("FFMPEG_PATH") is { Length: > 0 } env ? env
            : "ffmpeg";
}
