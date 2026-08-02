using System.Diagnostics;
using System.Globalization;
using System.IO;
using LensFlow.Core.Export;
using LensFlow.Core.Models;

namespace LensFlow.App.Exporting;

public sealed class FfmpegExporter
{
    private readonly FfmpegFilterBuilder _filterBuilder = new();

    public string? FindExecutable()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe");
        if (File.Exists(bundled))
        {
            return bundled;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var development = Path.Combine(current.FullName, "tools", "ffmpeg", "bin", "ffmpeg.exe");
            if (File.Exists(development))
            {
                return development;
            }

            current = current.Parent;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, "ffmpeg.exe"))
            .FirstOrDefault(File.Exists);
    }

    public async Task ExportAsync(
        LensFlowProject project,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var executable = FindExecutable()
            ?? throw new FileNotFoundException(
                "FFmpeg is not installed. Run scripts\\setup-ffmpeg.ps1 before exporting.");
        if (!File.Exists(project.MediaPath))
        {
            throw new FileNotFoundException("The source recording is missing.", project.MediaPath);
        }

        var startMs = Math.Clamp(project.Edit.TrimStartMs, 0, project.DurationMs);
        var endMs = project.Edit.TrimEndMs > startMs
            ? Math.Min(project.Edit.TrimEndMs, project.DurationMs)
            : project.DurationMs;
        var durationMs = Math.Max(1, endMs - startMs);
        var filter = _filterBuilder.BuildVideoFilter(project);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        AddArguments(
            startInfo,
            "-y",
            "-hide_banner",
            "-loglevel", "error",
            "-progress", "pipe:1",
            "-ss", Seconds(startMs),
            "-i", project.MediaPath,
            "-t", Seconds(durationMs),
            "-filter:v", filter,
            "-map", "0:v:0",
            "-map", "0:a?",
            "-c:v", "h264_mf",
            "-b:v", project.FrameRate >= 60 ? "18M" : "12M",
            "-c:a", "aac",
            "-b:a", "192k",
            "-movflags", "+faststart",
            "-shortest",
            outputPath);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        });

        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("out_time_us=", StringComparison.Ordinal) ||
                !long.TryParse(line.AsSpan("out_time_us=".Length), out var microseconds))
            {
                continue;
            }

            progress?.Report(Math.Clamp(microseconds / (durationMs * 1000d), 0, 1));
        }

        await process.WaitForExitAsync(cancellationToken);
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error) ? "FFmpeg export failed." : error.Trim());
        }

        progress?.Report(1);
    }

    private static void AddArguments(ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }

    private static string Seconds(long milliseconds)
        => (milliseconds / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
}
