using System.Globalization;
using LensFlow.Core.Editing;
using LensFlow.Core.Models;

namespace LensFlow.Core.Export;

public sealed class FfmpegFilterBuilder
{
    private const double EaseInSeconds = CameraEvaluator.EaseInMs / 1000d;
    private const double EaseOutSeconds = CameraEvaluator.EaseOutMs / 1000d;

    public string BuildVideoFilter(LensFlowProject project)
    {
        var trimEndMs = project.Edit.TrimEndMs > project.Edit.TrimStartMs
            ? project.Edit.TrimEndMs
            : project.DurationMs;
        var outputDuration = (trimEndMs - project.Edit.TrimStartMs) / 1000d;
        var width = MakeEven(project.SourceWidth);
        var height = MakeEven(project.SourceHeight);
        var shots = project.CameraShots
            .Where(shot =>
                shot.EndMs + CameraEvaluator.EaseOutMs > project.Edit.TrimStartMs &&
                shot.StartMs < trimEndMs)
            .OrderBy(shot => shot.StartMs)
            .ThenBy(shot => shot.EndMs)
            .ToArray();

        var zoomExpression = "1";
        var centerXExpression = "0.5";
        var centerYExpression = "0.5";

        for (var index = 0; index < shots.Length; index++)
        {
            var shot = shots[index];
            var rawStart = (shot.StartMs - project.Edit.TrimStartMs) / 1000d;
            var rawEnd = (shot.EndMs - project.Edit.TrimStartMs) / 1000d;
            var start = Math.Max(0, rawStart);
            var end = Math.Min(outputDuration, rawEnd);
            var targetZoom = Math.Clamp(shot.Zoom, 1, 3);
            var previousZoom = index > 0 && shots[index - 1].EndMs == shot.StartMs
                ? Math.Clamp(shots[index - 1].Zoom, 1, 3)
                : 1;
            var enterProgress =
                $"min(1,max(0,(in_time-{F(rawStart)})/{F(EaseInSeconds)}))";
            var shotZoom =
                $"{F(previousZoom)}+({F(targetZoom - previousZoom)})*{SmoothStep(enterProgress)}";
            var shotX = BuildCenterExpression(shot.Points, project.Edit.TrimStartMs, point => point.X);
            var shotY = BuildCenterExpression(shot.Points, project.Edit.TrimStartMs, point => point.Y);

            var touchesNext = index + 1 < shots.Length &&
                              shot.EndMs == shots[index + 1].StartMs;
            var exitStart = Math.Max(0, rawEnd);
            var exitEnd = Math.Min(outputDuration, rawEnd + EaseOutSeconds);
            if (!touchesNext && exitEnd > exitStart)
            {
                var exiting = $"between(in_time,{F(exitStart)},{F(exitEnd)})";
                var exitProgress =
                    $"min(1,max(0,(in_time-{F(rawEnd)})/{F(EaseOutSeconds)}))";
                var exitZoom =
                    $"{F(targetZoom)}+({F(1 - targetZoom)})*{SmoothStep(exitProgress)}";
                zoomExpression = $"if({exiting},{exitZoom},{zoomExpression})";
                centerXExpression = $"if({exiting},{shotX},{centerXExpression})";
                centerYExpression = $"if({exiting},{shotY},{centerYExpression})";
            }

            if (end > start)
            {
                var active = $"between(in_time,{F(start)},{F(end)})";
                zoomExpression = $"if({active},{shotZoom},{zoomExpression})";
                centerXExpression = $"if({active},{shotX},{centerXExpression})";
                centerYExpression = $"if({active},{shotY},{centerYExpression})";
            }
        }

        var xExpression =
            $"max(0,min(iw-iw/zoom,({centerXExpression})*iw-iw/(2*zoom)))";
        var yExpression =
            $"max(0,min(ih-ih/zoom,({centerYExpression})*ih-ih/(2*zoom)))";

        var filters = new List<string>
        {
            $"trim=start=0:end={F(outputDuration)}",
            "setpts=PTS-STARTPTS",
            $"zoompan=z='{zoomExpression}':x='{xExpression}':y='{yExpression}':d=1:s={width}x{height}:fps={project.FrameRate}",
        };

        var outputSize = GetOutputSize(project);
        if (outputSize.Width != width || outputSize.Height != height)
        {
            filters.Add(
                $"scale={outputSize.Width}:{outputSize.Height}:force_original_aspect_ratio=decrease");
            filters.Add(
                $"pad={outputSize.Width}:{outputSize.Height}:(ow-iw)/2:(oh-ih)/2:color=0x111318");
        }

        filters.Add("format=yuv420p");
        return string.Join(',', filters);
    }

    private static string BuildCenterExpression(
        IReadOnlyList<CameraPoint> points,
        long trimStartMs,
        Func<CameraPoint, double> selector)
    {
        if (points.Count == 0)
        {
            return "0.5";
        }

        var ordered = points.OrderBy(point => point.TimeMs).ToArray();
        var result = F(selector(ordered[^1]));

        for (var index = ordered.Length - 2; index >= 0; index--)
        {
            var current = ordered[index];
            var next = ordered[index + 1];
            var currentTime = Math.Max(0, (current.TimeMs - trimStartMs) / 1000d);
            var nextTime = Math.Max(currentTime + 0.001, (next.TimeMs - trimStartMs) / 1000d);
            var startValue = selector(current);
            var delta = selector(next) - startValue;
            var interpolation =
                $"{F(startValue)}+({F(delta)})*clip((in_time-{F(currentTime)})/{F(nextTime - currentTime)},0,1)";
            result = $"if(lt(in_time,{F(nextTime)}),{interpolation},{result})";
        }

        return result;
    }

    private static int MakeEven(int value) => Math.Max(2, value - (value % 2));

    private static string SmoothStep(string progress)
        => $"({progress})*({progress})*(3-2*({progress}))";

    private static (int Width, int Height) GetOutputSize(LensFlowProject project)
    {
        return project.Canvas.AspectRatio switch
        {
            AspectRatioPreset.Landscape16By9 => (1920, 1080),
            AspectRatioPreset.Square => (1080, 1080),
            AspectRatioPreset.Standard4By3 => (1440, 1080),
            AspectRatioPreset.Portrait9By16 => (608, 1080),
            _ => (MakeEven(project.SourceWidth), MakeEven(project.SourceHeight))
        };
    }

    private static string F(double value)
        => value.ToString("0.######", CultureInfo.InvariantCulture);
}
