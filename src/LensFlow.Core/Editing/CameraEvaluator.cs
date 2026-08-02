using LensFlow.Core.Models;

namespace LensFlow.Core.Editing;

public sealed class CameraEvaluator
{
    public CameraFrame Evaluate(IReadOnlyList<CameraShot> shots, long timeMs)
    {
        var shot = shots.FirstOrDefault(item => timeMs >= item.StartMs && timeMs <= item.EndMs);
        if (shot is null)
        {
            return CameraFrame.Wide;
        }

        var center = EvaluateCenter(shot.Points, timeMs);
        var zoom = CameraZoomMotion.Evaluate(shot, timeMs);
        return new CameraFrame(center.X, center.Y, zoom);
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
