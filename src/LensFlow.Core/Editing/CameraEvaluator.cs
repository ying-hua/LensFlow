using LensFlow.Core.Models;

namespace LensFlow.Core.Editing;

public sealed class CameraEvaluator
{
    public CameraFrame Evaluate(IReadOnlyList<CameraShot> shots, long timeMs)
    {
        var ordered = shots
            .Where(shot => shot.EndMs >= shot.StartMs)
            .OrderBy(shot => shot.StartMs)
            .ThenBy(shot => shot.EndMs)
            .ToArray();
        var activeIndex = -1;

        for (var index = 0; index < ordered.Length; index++)
        {
            if (timeMs >= ordered[index].StartMs && timeMs <= ordered[index].EndMs)
            {
                activeIndex = index;
            }
        }

        if (activeIndex >= 0)
        {
            var shot = ordered[activeIndex];
            var previousZoom = activeIndex > 0 &&
                               ordered[activeIndex - 1].EndMs == shot.StartMs
                ? Math.Clamp(ordered[activeIndex - 1].Zoom, 1, 3)
                : 1;
            var center = EvaluateCenter(shot.Points, timeMs);
            return new CameraFrame(
                center.X,
                center.Y,
                CameraZoomMotion.EvaluateEntry(previousZoom, shot, timeMs));
        }

        CameraShot? exitingShot = null;
        foreach (var shot in ordered)
        {
            if (timeMs > shot.EndMs &&
                timeMs - shot.EndMs <= CameraMotionDefaults.ZoomOutDurationMs)
            {
                exitingShot = shot;
            }
        }

        if (exitingShot is null)
        {
            return CameraFrame.Wide;
        }

        var exitCenter = EvaluateCenter(exitingShot.Points, exitingShot.EndMs);
        return new CameraFrame(
            exitCenter.X,
            exitCenter.Y,
            CameraZoomMotion.EvaluateExit(exitingShot, timeMs));
    }

    private static CameraPoint EvaluateCenter(IReadOnlyList<CameraPoint> points, long timeMs)
    {
        if (points.Count == 0)
        {
            return new CameraPoint(timeMs, 0.5, 0.5);
        }

        if (timeMs <= points[0].TimeMs)
        {
            return points[0];
        }

        for (var index = 1; index < points.Count; index++)
        {
            var next = points[index];
            if (timeMs > next.TimeMs)
            {
                continue;
            }

            var previous = points[index - 1];
            var duration = Math.Max(1, next.TimeMs - previous.TimeMs);
            var progress = Math.Clamp(
                (double)(timeMs - previous.TimeMs) / duration,
                0,
                1);
            return new CameraPoint(
                timeMs,
                Lerp(previous.X, next.X, progress),
                Lerp(previous.Y, next.Y, progress));
        }

        return points[^1];
    }

    private static double Lerp(double start, double end, double progress)
        => start + ((end - start) * progress);
}
