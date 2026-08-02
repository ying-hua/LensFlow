using LensFlow.Core.Models;

namespace LensFlow.Core.Editing;

public sealed class CameraEvaluator
{
    private const long EaseInMs = 350;
    private const long EaseOutMs = 400;

    public CameraFrame Evaluate(IReadOnlyList<CameraShot> shots, long timeMs)
    {
        var shot = shots.FirstOrDefault(item => timeMs >= item.StartMs && timeMs <= item.EndMs);
        if (shot is null)
        {
            return CameraFrame.Wide;
        }

        var center = EvaluateCenter(shot.Points, timeMs);
        var enter = SmoothStep(Math.Clamp((double)(timeMs - shot.StartMs) / EaseInMs, 0, 1));
        var exit = SmoothStep(Math.Clamp((double)(shot.EndMs - timeMs) / EaseOutMs, 0, 1));
        var envelope = Math.Min(enter, exit);
        var zoom = 1 + ((Math.Clamp(shot.Zoom, 1, 3) - 1) * envelope);
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
            var progress = SmoothStep((double)(timeMs - previous.TimeMs) / duration);
            return new CameraPoint(
                timeMs,
                Lerp(previous.X, next.X, progress),
                Lerp(previous.Y, next.Y, progress));
        }

        return points[^1];
    }

    private static double SmoothStep(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value * value * (3 - (2 * value));
    }

    private static double Lerp(double start, double end, double progress)
        => start + ((end - start) * progress);
}
