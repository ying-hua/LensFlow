using LensFlow.Core.Models;

namespace LensFlow.Core.Editing;

public sealed class AutoDirectorOptions
{
    public long PreRollMs { get; init; } = 200;
    public long HoldAfterClickMs { get; init; } = 1500;
    public long MergeGapMs { get; init; } = 1200;
    public double DefaultZoom { get; init; } = 1.6;
    public double SafeZoneEdgeRatio { get; init; } = CameraMotionDefaults.SafeZoneEdgeRatio;
    public double CursorVisibilityEdgeRatio { get; init; } =
        CameraMotionDefaults.CursorVisibilityEdgeRatio;
    public double SpringMass { get; init; } = CameraMotionDefaults.SpringMass;
    public double SpringStiffness { get; init; } = CameraMotionDefaults.SpringStiffness;
    public double SpringDamping { get; init; } = CameraMotionDefaults.SpringDamping;
    public double PathSimplificationTolerance { get; init; } =
        CameraMotionDefaults.PathSimplificationTolerance;
}

public sealed class AutoDirector
{
    private readonly AutoDirectorOptions _options;
    private readonly CameraPathBuilder _cameraPathBuilder;

    public AutoDirector(AutoDirectorOptions? options = null)
    {
        _options = options ?? new AutoDirectorOptions();
        _cameraPathBuilder = new CameraPathBuilder(_options);
    }

    public IReadOnlyList<CameraShot> Generate(
        IEnumerable<MouseSample> samples,
        long durationMs,
        int frameRate = 30)
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
            .Select(segment => CreateShot(segment, ordered, frameRate))
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

    private CameraShot CreateShot(
        FocusSegment segment,
        MouseSample[] samples,
        int frameRate)
    {
        return new CameraShot
        {
            StartMs = segment.StartMs,
            EndMs = segment.EndMs,
            Zoom = _options.DefaultZoom,
            Points = _cameraPathBuilder.Build(
                samples,
                segment.StartMs,
                segment.EndMs,
                _options.DefaultZoom,
                frameRate)
        };
    }

    private sealed class FocusSegment
    {
        public long StartMs { get; init; }
        public long EndMs { get; set; }
        public long LastClickMs { get; set; }
        public List<MouseSample> Clicks { get; init; } = [];
    }
}
