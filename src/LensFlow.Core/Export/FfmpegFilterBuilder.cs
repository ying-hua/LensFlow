using System.Globalization;
using System.Text;
using LensFlow.Core.Editing;
using LensFlow.Core.Models;

namespace LensFlow.Core.Export;

public sealed class FfmpegFilterBuilder
{
    private const double EaseInSeconds =
        CameraMotionDefaults.ZoomInDurationMs / 1000d;
    private const double EaseOutSeconds =
        CameraMotionDefaults.ZoomOutDurationMs / 1000d;

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
                shot.EndMs + CameraMotionDefaults.ZoomOutDurationMs >
                    project.Edit.TrimStartMs &&
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
            var entryDurationSeconds = Math.Max(
                0.001,
                Math.Min(EaseInSeconds, (shot.EndMs - shot.StartMs) / 1000d));
            var enterProgress =
                $"min(1,max(0,(in_time-({F(rawStart)}))/{F(entryDurationSeconds)}))";
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
                    $"min(1,max(0,(in_time-({F(rawEnd)}))/{F(EaseOutSeconds)}))";
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

        // Emitted as a sum of clipped ramps instead of a chain of nested
        // if(lt(...)) terms. Both describe the same piecewise-linear path: a
        // segment's clip() is 0 before it starts and 1 once it ends, so the
        // ramps accumulate to exactly the value of the last point already
        // passed.
        //
        // The sum is then folded into a balanced tree rather than left as a
        // flat "a+b+c+..." chain, because FFmpeg's parse_expr() recurses once
        // per '+' just as it does per nested call and bails out past roughly
        // 90 levels with "Missing ')' or too many args". A camera path holds
        // one point per frame, so either flat form broke every export of a
        // recording longer than ~3 seconds. Balancing makes the parser depth
        // logarithmic in the point count, which no realistic recording reaches.
        var terms = new List<string>(ordered.Length) { F(selector(ordered[0])) };

        for (var index = 1; index < ordered.Length; index++)
        {
            var previous = ordered[index - 1];
            var current = ordered[index];
            var previousTime = (previous.TimeMs - trimStartMs) / 1000d;
            var currentTime = Math.Max(
                previousTime + 0.001,
                (current.TimeMs - trimStartMs) / 1000d);
            var delta = selector(current) - selector(previous);
            terms.Add(
                $"({F(delta)})*clip((in_time-({F(previousTime)}))/{F(currentTime - previousTime)},0,1)");
        }

        return BalancedSum(terms);
    }

    private static string BalancedSum(List<string> terms)
    {
        var builder = new StringBuilder();
        while (terms.Count > 1)
        {
            var folded = new List<string>((terms.Count + 1) / 2);
            for (var index = 0; index < terms.Count; index += 2)
            {
                if (index + 1 < terms.Count)
                {
                    builder.Clear()
                        .Append('(').Append(terms[index])
                        .Append('+').Append(terms[index + 1])
                        .Append(')');
                    folded.Add(builder.ToString());
                }
                else
                {
                    folded.Add(terms[index]);
                }
            }

            terms = folded;
        }

        return terms[0];
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
