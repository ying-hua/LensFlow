using LensFlow.Core.Models;

namespace LensFlow.Core.Editing;

public sealed class CameraPathBuilder
{
    private readonly AutoDirectorOptions _options;

    public CameraPathBuilder(AutoDirectorOptions? options = null)
    {
        _options = options ?? new AutoDirectorOptions();
    }

    public List<CameraPoint> Build(
        IEnumerable<MouseSample> samples,
        long startMs,
        long endMs)
    {
        if (endMs <= startMs)
        {
            return [];
        }

        var ordered = samples.OrderBy(sample => sample.TimeMs).ToArray();
        var startPosition = EvaluatePosition(ordered, startMs);
        var points = new List<CameraPoint>
        {
            new(startMs, startPosition.X, startPosition.Y)
        };

        var smoothedX = startPosition.X;
        var smoothedY = startPosition.Y;
        var lastPointTime = startMs;
        foreach (var sample in ordered.Where(sample =>
                     sample.TimeMs > startMs &&
                     sample.TimeMs <= endMs))
        {
            var isClick = sample.Kind is MouseEventKind.LeftClick or MouseEventKind.RightClick;
            if (!isClick && sample.TimeMs - lastPointTime < _options.FollowSampleIntervalMs)
            {
                continue;
            }

            var distance = Distance(smoothedX, smoothedY, sample.X, sample.Y);
            if (!isClick && distance < _options.FollowDeadZone)
            {
                continue;
            }

            var alpha = isClick ? 0.7 : _options.FollowSmoothing;
            smoothedX += (sample.X - smoothedX) * alpha;
            smoothedY += (sample.Y - smoothedY) * alpha;
            points.Add(new CameraPoint(sample.TimeMs, smoothedX, smoothedY));
            lastPointTime = sample.TimeMs;
        }

        if (points[^1].TimeMs < endMs)
        {
            points.Add(new CameraPoint(endMs, smoothedX, smoothedY));
        }

        return points;
    }

    private static (double X, double Y) EvaluatePosition(
        IReadOnlyList<MouseSample> samples,
        long timeMs)
    {
        if (samples.Count == 0)
        {
            return (0.5, 0.5);
        }

        if (timeMs <= samples[0].TimeMs)
        {
            return (samples[0].X, samples[0].Y);
        }

        for (var index = 1; index < samples.Count; index++)
        {
            var next = samples[index];
            if (timeMs > next.TimeMs)
            {
                continue;
            }

            var previous = samples[index - 1];
            var progress = (double)(timeMs - previous.TimeMs) /
                           Math.Max(1, next.TimeMs - previous.TimeMs);
            return (
                previous.X + ((next.X - previous.X) * progress),
                previous.Y + ((next.Y - previous.Y) * progress));
        }

        return (samples[^1].X, samples[^1].Y);
    }

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
