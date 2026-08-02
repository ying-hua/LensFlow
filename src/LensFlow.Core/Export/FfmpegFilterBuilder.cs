using System.Globalization;
using LensFlow.Core.Editing;
using LensFlow.Core.Models;

namespace LensFlow.Core.Export;

public sealed class FfmpegFilterBuilder
{
    public string BuildVideoFilter(LensFlowProject project)
    {
        var trimEndMs = project.Edit.TrimEndMs > project.Edit.TrimStartMs
            ? project.Edit.TrimEndMs
            : project.DurationMs;
        var outputDuration = (trimEndMs - project.Edit.TrimStartMs) / 1000d;
        var width = MakeEven(project.SourceWidth);
        var height = MakeEven(project.SourceHeight);
        var shots = project.CameraShots
            .Where(shot => shot.EndMs > project.Edit.TrimStartMs && shot.StartMs < trimEndMs)
            .OrderBy(shot => shot.StartMs)
            .ToArray();

        var zoomExpression = "1";
        var centerXExpression = "0.5";
        var centerYExpression = "0.5";

        for (var index = shots.Length - 1; index >= 0; index--)
        {
            var shot = shots[index];
            var start = Math.Max(0, (shot.StartMs - project.Edit.TrimStartMs) / 1000d);
            var end = Math.Min(outputDuration, (shot.EndMs - project.Edit.TrimStartMs) / 1000d);
            if (end <= start)
            {
                continue;
            }

            var active = $"between(in_time,{F(start)},{F(end)})";
            var shotZoom = BuildZoomExpression(
                shot,
                project.Edit.TrimStartMs,
                project.FrameRate);
            var shotX = BuildCenterExpression(shot.Points, project.Edit.TrimStartMs, point => point.X);
            var shotY = BuildCenterExpression(shot.Points, project.Edit.TrimStartMs, point => point.Y);

            zoomExpression = $"if({active},{shotZoom},{zoomExpression})";
            centerXExpression = $"if({active},{shotX},{centerXExpression})";
            centerYExpression = $"if({active},{shotY},{centerYExpression})";
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

    private static string BuildZoomExpression(
        CameraShot shot,
        long trimStartMs,
        int frameRate)
    {
        var points = CameraZoomMotion.BuildPoints(shot, frameRate);
        if (points.Count == 0)
        {
            return "1";
        }

        var result = F(points[^1].Zoom);
        for (var index = points.Count - 2; index >= 0; index--)
        {
            var current = points[index];
            var next = points[index + 1];
            var currentTime = Math.Max(0, (current.TimeMs - trimStartMs) / 1000d);
            var nextTime = Math.Max(
                currentTime + 0.001,
                (next.TimeMs - trimStartMs) / 1000d);
            var delta = next.Zoom - current.Zoom;
            var interpolation =
                $"{F(current.Zoom)}+({F(delta)})*clip((in_time-{F(currentTime)})/{F(nextTime - currentTime)},0,1)";
            result = $"if(lt(in_time,{F(nextTime)}),{interpolation},{result})";
        }

        return result;
    }

    private static int MakeEven(int value) => Math.Max(2, value - (value % 2));

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
