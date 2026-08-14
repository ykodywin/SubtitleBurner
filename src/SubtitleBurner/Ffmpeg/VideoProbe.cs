using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SubtitleBurner.Ffmpeg;

/// <summary>Basic facts about a video file, probed via ffmpeg itself (no ffprobe needed).</summary>
/// <param name="Width">Frame width in px.</param>
/// <param name="Height">Frame height in px.</param>
/// <param name="DurationSeconds">Container duration in seconds.</param>
public record VideoInfo(int Width, int Height, double DurationSeconds);

/// <summary>
/// Probes video size/duration by parsing `ffmpeg -i` stderr
/// ("Duration: 00:00:06.00", "Video: ... 640x360 ...").
/// </summary>
public static partial class VideoProbe
{
    /// <summary>Probe a video file; null when ffmpeg can't read it or the output doesn't parse.</summary>
    public static async Task<VideoInfo?> GetInfoAsync(string ffmpegPath, string videoPath, CancellationToken ct = default)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            process.StartInfo.ArgumentList.Add("-hide_banner");
            process.StartInfo.ArgumentList.Add("-i");
            process.StartInfo.ArgumentList.Add(videoPath);
            process.Start();
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            var size = VideoStreamRegex().Match(stderr);
            var duration = DurationRegex().Match(stderr);
            if (!size.Success || !duration.Success)
                return null;

            var w = int.Parse(size.Groups[1].Value, CultureInfoSafe);
            var h = int.Parse(size.Groups[2].Value, CultureInfoSafe);
            var d = TimeSpan.ParseExact(duration.Groups[1].Value, @"hh\:mm\:ss\.ff", CultureInfoSafe).TotalSeconds;
            return new VideoInfo(w, h, d);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static readonly System.Globalization.CultureInfo CultureInfoSafe = System.Globalization.CultureInfo.InvariantCulture;

    [GeneratedRegex(@"Video:.*?(\d{2,5})x(\d{2,5})")]
    private static partial Regex VideoStreamRegex();

    [GeneratedRegex(@"Duration: (\d{2}:\d{2}:\d{2}\.\d{2})")]
    private static partial Regex DurationRegex();
}
