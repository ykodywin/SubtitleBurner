using System.Diagnostics;
using System.Text;
using SubtitleBurner.Rendering;

namespace SubtitleBurner.Ffmpeg;

/// <summary>
/// One-call burn: video + word timings → mp4 with subtitles burned in.
/// Renders PNG overlays into a temp dir, builds the overlay filter graph and
/// runs ffmpeg. For integration into a larger filter pipeline use
/// <see cref="SubtitleRenderer"/> + <see cref="FilterGraphBuilder"/> directly.
/// </summary>
public static class SubtitleBurner
{
    private static readonly string[] DefaultEncoderArgs = ["-c:v", "libx264", "-preset", "fast", "-crf", "23"];
    private static readonly string[] DefaultAudioArgs = ["-c:a", "aac", "-b:a", "192k"];

    /// <summary>
    /// Burn karaoke subtitles from per-word timings. Boxed vs pop follows the
    /// preset's UseBox; windows above <see cref="BurnOptions.KaraokeWordCap"/>
    /// degrade to regular blocks.
    /// </summary>
    /// <returns>Facts about the produced file.</returns>
    public static async Task<BurnResult> BurnAsync(
        string inputVideo,
        string outputVideo,
        IReadOnlyList<SubtitleWord> words,
        SubtitleStyle? style = null,
        BurnOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new BurnOptions();
        var ffmpeg = FfmpegLocator.ResolveFfmpeg(options.FfmpegPath);
        var info = await VideoProbe.GetInfoAsync(ffmpeg, inputVideo, ct)
            ?? throw new SubtitleBurnerException($"Could not probe '{inputVideo}' with '{ffmpeg}'.");

        var preset = SubtitleStyles.Resolve(style?.Preset);
        var renderOpts = new RenderOptions(info.Width, info.Height,
            options.IsKeyword, options.KeywordEmoji, options.SpeakerPalette);

        var tempDir = Path.Combine(Path.GetTempPath(), "subtitleburner-" + Guid.NewGuid().ToString("N"));
        IReadOnlyList<SubtitleOverlay>? overlays = null;
        try
        {
            if (words.Count is > 0 && words.Count <= options.KaraokeWordCap)
            {
                overlays = preset.UseBox
                    ? SubtitleRenderer.RenderBoxedKaraoke(words, 0, info.DurationSeconds, renderOpts, tempDir, "sb", style)
                    : SubtitleRenderer.RenderPopKaraoke(words, 0, info.DurationSeconds, renderOpts, tempDir, "sb", style);
            }
            if (overlays is null && words.Count > 0)
            {
                // Over the karaoke cap: chunk words into plain lines (timing gaps
                // of >0.6s or 7 words per line, whichever comes first)
                var lines = ChunkWordsToLines(words);
                overlays = SubtitleRenderer.RenderBlocks(lines, 0, info.DurationSeconds, renderOpts, tempDir, "sb", style);
            }

            return await RunBurnAsync(ffmpeg, inputVideo, outputVideo, overlays ?? [],
                style, preset, info, options, tempDir, progress, ct);
        }
        finally
        {
            TryDeleteDir(tempDir);
        }
    }

    /// <summary>
    /// Burn regular (non-karaoke) subtitles from whole lines, e.g. user-edited
    /// transcript segments.
    /// </summary>
    public static async Task<BurnResult> BurnAsync(
        string inputVideo,
        string outputVideo,
        IReadOnlyList<SubtitleLine> lines,
        SubtitleStyle? style = null,
        BurnOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new BurnOptions();
        var ffmpeg = FfmpegLocator.ResolveFfmpeg(options.FfmpegPath);
        var info = await VideoProbe.GetInfoAsync(ffmpeg, inputVideo, ct)
            ?? throw new SubtitleBurnerException($"Could not probe '{inputVideo}' with '{ffmpeg}'.");

        var preset = SubtitleStyles.Resolve(style?.Preset);
        var renderOpts = new RenderOptions(info.Width, info.Height,
            options.IsKeyword, options.KeywordEmoji, options.SpeakerPalette);

        var tempDir = Path.Combine(Path.GetTempPath(), "subtitleburner-" + Guid.NewGuid().ToString("N"));
        try
        {
            var overlays = SubtitleRenderer.RenderBlocks(lines, 0, info.DurationSeconds, renderOpts, tempDir, "sb", style) ?? [];
            return await RunBurnAsync(ffmpeg, inputVideo, outputVideo, overlays,
                style, preset, info, options, tempDir, progress, ct);
        }
        finally
        {
            TryDeleteDir(tempDir);
        }
    }

    private static async Task<BurnResult> RunBurnAsync(
        string ffmpeg, string inputVideo, string outputVideo,
        IReadOnlyList<SubtitleOverlay> overlays,
        SubtitleStyle? style, SubtitleStylePreset preset, VideoInfo info,
        BurnOptions options, string tempDir,
        IProgress<double>? progress, CancellationToken ct)
    {
        var align = style?.Align ?? (SubtitleAlign)preset.Alignment;
        var graph = FilterGraphBuilder.Build(overlays, align, info.Height, options.BottomMarginRatio);
        var graphPath = Path.Combine(tempDir, "graph.txt");
        Directory.CreateDirectory(tempDir);
        await File.WriteAllTextAsync(graphPath, graph, ct);

        var args = BuildArgs(inputVideo, overlays, "graph.txt",
            options.EncoderArgs ?? DefaultEncoderArgs,
            options.AudioArgs ?? DefaultAudioArgs,
            outputVideo);

        var (exitCode, stderrTail) = await RunFfmpegAsync(
            ffmpeg, args, tempDir, info.DurationSeconds, progress, ct);
        if (exitCode != 0)
            throw new SubtitleBurnerException($"ffmpeg exited with {exitCode}. Stderr tail: {stderrTail}");

        return new BurnResult(outputVideo, info.Width, info.Height, info.DurationSeconds, overlays.Count);
    }

    /// <summary>
    /// Full ffmpeg argument list: base input, one input per overlay PNG (by file
    /// name — run with the PNG directory as working dir), graph from a script
    /// file (sidesteps the 32K command-line limit), map graph + optional audio.
    /// </summary>
    internal static List<string> BuildArgs(
        string inputVideo,
        IReadOnlyList<SubtitleOverlay> overlays,
        string graphScriptPath,
        IReadOnlyList<string> encoderArgs,
        IReadOnlyList<string> audioArgs,
        string outputVideo)
    {
        var args = new List<string> { "-y", "-i", inputVideo };
        // No -loop for PNGs: each is a single frame and overlay's eof_action=repeat
        // holds it — looping inputs would decode endlessly and OOM
        foreach (var ov in overlays)
            args.AddRange(["-i", Path.GetFileName(ov.PngPath)]);
        args.AddRange(["-nostats", "-progress", "pipe:1"]);
        args.AddRange(["-filter_complex_script", graphScriptPath]);
        args.AddRange(["-map", "[out]", "-map", "0:a?"]);
        args.AddRange(encoderArgs);
        args.AddRange(audioArgs);
        args.Add(outputVideo);
        return args;
    }

    private static async Task<(int ExitCode, string StderrTail)> RunFfmpegAsync(
        string ffmpeg, List<string> args, string workDir,
        double totalSeconds, IProgress<double>? progress, CancellationToken ct)
    {
        using var process = new Process();
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        process.StartInfo = psi;

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new SubtitleBurnerException($"Failed to start ffmpeg ('{ffmpeg}'): {ex.Message}");
        }

        var stderr = new StringBuilder();
        var stderrTask = Task.Run(async () =>
        {
            var buf = new char[4096];
            int read;
            while ((read = await process.StandardError.ReadAsync(buf, ct)) > 0)
            {
                if (stderr.Length > 8192) stderr.Remove(0, 4096); // keep the tail
                stderr.Append(buf, 0, read);
            }
        }, ct);

        // -progress pipe:1 emits "out_time_ms=<microseconds>" lines
        var stdoutTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
            {
                if (progress is not null && line.StartsWith("out_time_ms=", StringComparison.Ordinal)
                    && long.TryParse(line["out_time_ms=".Length..], out var us) && totalSeconds > 0)
                {
                    progress.Report(Math.Clamp(us / 1_000_000.0 / totalSeconds, 0, 1));
                }
            }
        }, ct);

        await process.WaitForExitAsync(ct);
        await Task.WhenAll(stderrTask, stdoutTask);
        progress?.Report(1.0);
        return (process.ExitCode, stderr.ToString().Trim());
    }

    private static List<SubtitleLine> ChunkWordsToLines(IReadOnlyList<SubtitleWord> words)
    {
        var lines = new List<SubtitleLine>();
        var chunk = new List<SubtitleWord>();
        foreach (var w in words)
        {
            if (chunk.Count > 0 && (chunk.Count >= 7 || w.Start - chunk[^1].End > 0.6))
            {
                lines.Add(new SubtitleLine(chunk[0].Start, chunk[^1].End, string.Join(' ', chunk.Select(c => c.Text))));
                chunk = [];
            }
            chunk.Add(w);
        }
        if (chunk.Count > 0)
            lines.Add(new SubtitleLine(chunk[0].Start, chunk[^1].End, string.Join(' ', chunk.Select(c => c.Text))));
        return lines;
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best effort — temp dir
        }
    }
}

/// <summary>Facts about a finished burn.</summary>
/// <param name="OutputPath">The produced mp4.</param>
/// <param name="Width">Frame width in px.</param>
/// <param name="Height">Frame height in px.</param>
/// <param name="DurationSeconds">Duration in seconds.</param>
/// <param name="OverlayCount">How many PNG overlay states were composited.</param>
public record BurnResult(string OutputPath, int Width, int Height, double DurationSeconds, int OverlayCount);
