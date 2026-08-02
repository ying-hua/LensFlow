using LensFlow.Core.Models;

namespace LensFlow.Core.Editing;

public sealed record CameraShotTimingState(
    Guid Id,
    long StartMs,
    long EndMs,
    bool UserLocked,
    IReadOnlyList<CameraPoint> Points);

public sealed class TimelineEditor
{
    public const long MinimumSegmentDurationMs = 100;

    private readonly CameraPathBuilder _cameraPathBuilder;

    public TimelineEditor(CameraPathBuilder? cameraPathBuilder = null)
    {
        _cameraPathBuilder = cameraPathBuilder ?? new CameraPathBuilder();
    }

    public bool SplitVideoSegment(
        IList<VideoSegment> segments,
        Guid selectedId,
        long playheadMs)
    {
        var index = FindIndex(segments, segment => segment.Id == selectedId);
        if (index < 0)
        {
            return false;
        }

        var selected = segments[index];
        if (playheadMs - selected.StartMs < MinimumSegmentDurationMs ||
            selected.EndMs - playheadMs < MinimumSegmentDurationMs)
        {
            return false;
        }

        var originalEnd = selected.EndMs;
        selected.EndMs = playheadMs;
        segments.Insert(index + 1, new VideoSegment
        {
            StartMs = playheadMs,
            EndMs = originalEnd
        });
        return true;
    }

    public bool SplitCameraShot(
        IList<CameraShot> shots,
        Guid selectedId,
        long playheadMs)
    {
        var index = FindIndex(shots, shot => shot.Id == selectedId);
        if (index < 0)
        {
            return false;
        }

        var selected = shots[index];
        if (playheadMs - selected.StartMs < MinimumSegmentDurationMs ||
            selected.EndMs - playheadMs < MinimumSegmentDurationMs)
        {
            return false;
        }

        var originalEnd = selected.EndMs;
        var boundary = InterpolatePoint(selected.Points, playheadMs);
        var leftPoints = selected.Points
            .Where(point => point.TimeMs < playheadMs)
            .Append(boundary)
            .OrderBy(point => point.TimeMs)
            .ToList();
        var rightPoints = selected.Points
            .Where(point => point.TimeMs > playheadMs)
            .Prepend(boundary)
            .OrderBy(point => point.TimeMs)
            .ToList();

        selected.EndMs = playheadMs;
        selected.Points = leftPoints;
        selected.UserLocked = true;
        shots.Insert(index + 1, new CameraShot
        {
            StartMs = playheadMs,
            EndMs = originalEnd,
            Zoom = selected.Zoom,
            UserLocked = true,
            Points = rightPoints
        });
        return true;
    }

    public CameraShotTimingState? CaptureCameraShotTiming(
        IReadOnlyList<CameraShot> shots,
        Guid selectedId)
    {
        var selected = shots.FirstOrDefault(shot => shot.Id == selectedId);
        return selected is null
            ? null
            : new CameraShotTimingState(
                selected.Id,
                selected.StartMs,
                selected.EndMs,
                selected.UserLocked,
                selected.Points
                    .Select(point => new CameraPoint(point.TimeMs, point.X, point.Y))
                    .ToArray());
    }

    public bool ApplyCameraShotTiming(
        IList<CameraShot> shots,
        CameraShotTimingState baseline,
        long startMs,
        long endMs,
        IReadOnlyList<MouseSample> mouseSamples,
        long timelineDurationMs)
    {
        var selected = shots.FirstOrDefault(shot => shot.Id == baseline.Id);
        if (selected is null ||
            startMs < 0 ||
            endMs > timelineDurationMs ||
            endMs - startMs < MinimumSegmentDurationMs ||
            shots.Any(shot =>
                shot.Id != baseline.Id &&
                shot.StartMs < endMs &&
                shot.EndMs > startMs))
        {
            return false;
        }

        var timingChanged =
            startMs != baseline.StartMs ||
            endMs != baseline.EndMs;
        var userLocked = timingChanged || baseline.UserLocked;
        var points = timingChanged
            ? _cameraPathBuilder.Build(mouseSamples, startMs, endMs)
            : baseline.Points
                .Select(point => new CameraPoint(point.TimeMs, point.X, point.Y))
                .ToList();
        var changed =
            selected.StartMs != startMs ||
            selected.EndMs != endMs ||
            selected.UserLocked != userLocked ||
            !selected.Points.SequenceEqual(points);

        selected.StartMs = startMs;
        selected.EndMs = endMs;
        selected.Points = points;
        selected.UserLocked = userLocked;
        return changed;
    }

    private static CameraPoint InterpolatePoint(IReadOnlyList<CameraPoint> points, long timeMs)
    {
        if (points.Count == 0)
        {
            return new CameraPoint(timeMs, 0.5, 0.5);
        }

        var ordered = points.OrderBy(point => point.TimeMs).ToArray();
        if (timeMs <= ordered[0].TimeMs)
        {
            return new CameraPoint(timeMs, ordered[0].X, ordered[0].Y);
        }

        for (var index = 1; index < ordered.Length; index++)
        {
            if (timeMs > ordered[index].TimeMs)
            {
                continue;
            }

            var previous = ordered[index - 1];
            var next = ordered[index];
            var progress = (double)(timeMs - previous.TimeMs) /
                           Math.Max(1, next.TimeMs - previous.TimeMs);
            return new CameraPoint(
                timeMs,
                previous.X + ((next.X - previous.X) * progress),
                previous.Y + ((next.Y - previous.Y) * progress));
        }

        return new CameraPoint(timeMs, ordered[^1].X, ordered[^1].Y);
    }

    private static int FindIndex<T>(IList<T> items, Func<T, bool> predicate)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (predicate(items[index]))
            {
                return index;
            }
        }

        return -1;
    }
}
