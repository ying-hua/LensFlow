using LensFlow.Core.Models;

namespace LensFlow.Core.Editing;

public readonly record struct CameraZoomPoint(long TimeMs, double Zoom);

public static class CameraZoomMotion
{
    public static double Evaluate(CameraShot shot, long timeMs)
    {
        if (timeMs <= shot.StartMs || timeMs >= shot.EndMs || shot.Zoom <= 1)
        {
            return 1;
        }

        var releaseTimeMs = GetReleaseTimeMs(shot);
        var entryState = EvaluateSpring(
            1,
            0,
            Math.Clamp(shot.Zoom, 1, 3),
            (Math.Min(timeMs, releaseTimeMs) - shot.StartMs) / 1000d);
        if (timeMs <= releaseTimeMs)
        {
            return Math.Max(1, entryState.Position);
        }

        var exitState = EvaluateSpring(
            entryState.Position,
            entryState.Velocity,
            1,
            (timeMs - releaseTimeMs) / 1000d);
        return Math.Max(1, exitState.Position);
    }

    public static IReadOnlyList<CameraZoomPoint> BuildPoints(
        CameraShot shot,
        int frameRate)
    {
        if (shot.EndMs <= shot.StartMs)
        {
            return [];
        }

        var samplingRate = Math.Clamp(Math.Max(60, frameRate), 30, 120);
        var points = new List<CameraZoomPoint>
        {
            new(shot.StartMs, 1)
        };
        var releaseTimeMs = GetReleaseTimeMs(shot);
        var stepIndex = 1;
        while (points[^1].TimeMs < shot.EndMs)
        {
            var timeMs = Math.Min(
                shot.EndMs,
                shot.StartMs + (long)Math.Round(stepIndex * 1000d / samplingRate));
            stepIndex++;
            if (timeMs <= points[^1].TimeMs)
            {
                continue;
            }

            if (points[^1].TimeMs < releaseTimeMs && timeMs > releaseTimeMs)
            {
                points.Add(new CameraZoomPoint(releaseTimeMs, Evaluate(shot, releaseTimeMs)));
            }

            points.Add(new CameraZoomPoint(timeMs, Evaluate(shot, timeMs)));
        }

        return Simplify(points, releaseTimeMs);
    }

    private static long GetReleaseTimeMs(CameraShot shot)
    {
        var durationMs = Math.Max(0, shot.EndMs - shot.StartMs);
        var releaseLeadMs = Math.Min(
            CameraMotionDefaults.ZoomReleaseLeadMs,
            durationMs / 2);
        return shot.EndMs - releaseLeadMs;
    }

    private static SpringState EvaluateSpring(
        double initialPosition,
        double initialVelocity,
        double target,
        double elapsedSeconds)
    {
        if (elapsedSeconds <= 0)
        {
            return new SpringState(initialPosition, initialVelocity);
        }

        var mass = CameraMotionDefaults.SpringMass;
        var stiffness = CameraMotionDefaults.SpringStiffness;
        var damping = CameraMotionDefaults.SpringDamping;
        var naturalFrequency = Math.Sqrt(stiffness / mass);
        var dampingRatio = damping / (2 * Math.Sqrt(stiffness * mass));
        var dampedFrequency =
            naturalFrequency * Math.Sqrt(Math.Max(0.000001, 1 - (dampingRatio * dampingRatio)));
        var displacement = initialPosition - target;
        var sineCoefficient =
            (initialVelocity + (dampingRatio * naturalFrequency * displacement)) /
            dampedFrequency;
        var decay = Math.Exp(-dampingRatio * naturalFrequency * elapsedSeconds);
        var cosine = Math.Cos(dampedFrequency * elapsedSeconds);
        var sine = Math.Sin(dampedFrequency * elapsedSeconds);
        var relativePosition =
            decay * ((displacement * cosine) + (sineCoefficient * sine));
        var relativeVelocity = decay *
            ((-dampingRatio * naturalFrequency *
              ((displacement * cosine) + (sineCoefficient * sine))) +
             (-displacement * dampedFrequency * sine) +
             (sineCoefficient * dampedFrequency * cosine));
        return new SpringState(target + relativePosition, relativeVelocity);
    }

    private static IReadOnlyList<CameraZoomPoint> Simplify(
        IReadOnlyList<CameraZoomPoint> points,
        long releaseTimeMs)
    {
        if (points.Count <= 2)
        {
            return points;
        }

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;
        for (var index = 1; index < points.Count - 1; index++)
        {
            keep[index] = points[index].TimeMs == releaseTimeMs;
        }

        var anchors = Enumerable.Range(0, points.Count)
            .Where(index => keep[index])
            .ToArray();
        for (var anchorIndex = 1; anchorIndex < anchors.Length; anchorIndex++)
        {
            SimplifyRange(points, anchors[anchorIndex - 1], anchors[anchorIndex], keep);
        }

        return points.Where((_, index) => keep[index]).ToArray();
    }

    private static void SimplifyRange(
        IReadOnlyList<CameraZoomPoint> points,
        int startIndex,
        int endIndex,
        bool[] keep)
    {
        var ranges = new Stack<(int Start, int End)>();
        ranges.Push((startIndex, endIndex));
        while (ranges.Count > 0)
        {
            var (start, end) = ranges.Pop();
            if (end - start <= 1)
            {
                continue;
            }

            var startPoint = points[start];
            var endPoint = points[end];
            var durationMs = Math.Max(1, endPoint.TimeMs - startPoint.TimeMs);
            var maxError = 0d;
            var maxIndex = -1;
            for (var index = start + 1; index < end; index++)
            {
                var progress =
                    (double)(points[index].TimeMs - startPoint.TimeMs) / durationMs;
                var expected =
                    startPoint.Zoom + ((endPoint.Zoom - startPoint.Zoom) * progress);
                var error = Math.Abs(points[index].Zoom - expected);
                if (error > maxError)
                {
                    maxError = error;
                    maxIndex = index;
                }
            }

            if (maxIndex < 0 ||
                maxError <= CameraMotionDefaults.PathSimplificationTolerance)
            {
                continue;
            }

            keep[maxIndex] = true;
            ranges.Push((start, maxIndex));
            ranges.Push((maxIndex, end));
        }
    }

    private readonly record struct SpringState(double Position, double Velocity);
}
