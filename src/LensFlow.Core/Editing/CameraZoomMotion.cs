using LensFlow.Core.Models;

namespace LensFlow.Core.Editing;

public readonly record struct CameraZoomPoint(long TimeMs, double Zoom);

public static class CameraZoomMotion
{
    public static double Evaluate(CameraShot shot, long timeMs)
    {
        if (timeMs < shot.StartMs ||
            timeMs > shot.EndMs + CameraMotionDefaults.ZoomOutDurationMs ||
            shot.Zoom <= 1)
        {
            return 1;
        }

        return timeMs <= shot.EndMs
            ? EvaluateEntry(1, shot, timeMs)
            : EvaluateExit(shot, timeMs);
    }

    public static double EvaluateEntry(
        double initialZoom,
        CameraShot shot,
        long timeMs)
    {
        var entryDurationMs = Math.Max(
            1,
            Math.Min(
                CameraMotionDefaults.ZoomInDurationMs,
                shot.EndMs - shot.StartMs));
        var progress = SmoothStep(
            (double)(timeMs - shot.StartMs) /
            entryDurationMs);
        return Lerp(
            Math.Clamp(initialZoom, 1, 3),
            Math.Clamp(shot.Zoom, 1, 3),
            progress);
    }

    public static double EvaluateExit(CameraShot shot, long timeMs)
    {
        var progress = SmoothStep(
            (double)(timeMs - shot.EndMs) /
            CameraMotionDefaults.ZoomOutDurationMs);
        return Lerp(Math.Clamp(shot.Zoom, 1, 3), 1, progress);
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
        var motionEndMs = shot.EndMs + CameraMotionDefaults.ZoomOutDurationMs;
        var stepIndex = 1;
        while (points[^1].TimeMs < motionEndMs)
        {
            var timeMs = Math.Min(
                motionEndMs,
                shot.StartMs + (long)Math.Round(stepIndex * 1000d / samplingRate));
            stepIndex++;
            if (timeMs <= points[^1].TimeMs)
            {
                continue;
            }

            if (points[^1].TimeMs < shot.EndMs && timeMs > shot.EndMs)
            {
                points.Add(new CameraZoomPoint(shot.EndMs, Evaluate(shot, shot.EndMs)));
            }

            points.Add(new CameraZoomPoint(timeMs, Evaluate(shot, timeMs)));
        }

        return Simplify(points, shot.EndMs);
    }

    private static IReadOnlyList<CameraZoomPoint> Simplify(
        IReadOnlyList<CameraZoomPoint> points,
        long shotEndMs)
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
            keep[index] = points[index].TimeMs == shotEndMs;
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

    private static double SmoothStep(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value * value * (3 - (2 * value));
    }

    private static double Lerp(double start, double end, double progress)
        => start + ((end - start) * progress);
}
