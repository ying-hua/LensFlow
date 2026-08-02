using LensFlow.Core.Models;

namespace LensFlow.Core.Editing;

public sealed class AutoDirectorOptions
{
    public long PreRollMs { get; init; } = 200;
    public long HoldAfterClickMs { get; init; } = 1500;
    public long MergeGapMs { get; init; } = 1200;
    public long FollowSampleIntervalMs { get; init; } = 150;
    public double FollowDeadZone { get; init; } = 0.06;
    public double FollowSmoothing { get; init; } = 0.35;
    public double DefaultZoom { get; init; } = 1.6;
}

public sealed class AutoDirector
{
    private readonly AutoDirectorOptions _options;

    public AutoDirector(AutoDirectorOptions? options = null)
    {
        _options = options ?? new AutoDirectorOptions();
    }

    public IReadOnlyList<CameraShot> Generate(
        IEnumerable<MouseSample> samples,
        long durationMs)
    {
        var ordered = samples.OrderBy(sample => sample.TimeMs).ToArray();
        var clicks = ordered
            .Where(sample => sample.Kind is MouseEventKind.LeftClick or MouseEventKind.RightClick)
            .ToArray();

        if (clicks.Length == 0 || durationMs <= 0)
        {
            return [];
        }

        var segments = BuildSegments(clicks, durationMs);
        return segments
            .Select(segment => CreateShot(segment, ordered))
            .ToArray();
    }

    private List<FocusSegment> BuildSegments(MouseSample[] clicks, long durationMs)
    {
        var segments = new List<FocusSegment>();

        foreach (var click in clicks)
        {
            var end = Math.Min(durationMs, click.TimeMs + _options.HoldAfterClickMs);
            if (segments.Count > 0)
            {
                var current = segments[^1];
                if (click.TimeMs - current.LastClickMs <= _options.MergeGapMs)
                {
                    current.EndMs = Math.Max(current.EndMs, end);
                    current.LastClickMs = click.TimeMs;
                    current.Clicks.Add(click);
                    continue;
                }
            }

            segments.Add(new FocusSegment
            {
                StartMs = Math.Max(0, click.TimeMs - _options.PreRollMs),
                EndMs = end,
                LastClickMs = click.TimeMs,
                Clicks = [click]
            });
        }

        return segments;
    }

    private CameraShot CreateShot(FocusSegment segment, MouseSample[] samples)
    {
        var candidates = samples
            .Where(sample => sample.TimeMs >= segment.StartMs && sample.TimeMs <= segment.EndMs)
            .OrderBy(sample => sample.TimeMs)
            .ToArray();

        var first = segment.Clicks[0];
        var points = new List<CameraPoint>
        {
            new(segment.StartMs, first.X, first.Y)
        };

        var smoothedX = first.X;
        var smoothedY = first.Y;
        var lastPointTime = segment.StartMs;

        foreach (var sample in candidates)
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

        if (points[^1].TimeMs < segment.EndMs)
        {
            points.Add(new CameraPoint(segment.EndMs, smoothedX, smoothedY));
        }

        return new CameraShot
        {
            StartMs = segment.StartMs,
            EndMs = segment.EndMs,
            Zoom = _options.DefaultZoom,
            Points = points
        };
    }

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private sealed class FocusSegment
    {
        public long StartMs { get; init; }
        public long EndMs { get; set; }
        public long LastClickMs { get; set; }
        public List<MouseSample> Clicks { get; init; } = [];
    }
}
