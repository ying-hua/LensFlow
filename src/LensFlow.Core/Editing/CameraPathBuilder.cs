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
        long endMs,
        double zoom,
        int frameRate)
    {
        if (endMs <= startMs)
        {
            return [];
        }

        var ordered = samples.OrderBy(sample => sample.TimeMs).ToArray();
        var clampedZoom = Math.Clamp(zoom, 1, 3);
        var viewportHalfSize = 0.5 / clampedZoom;
        var safeEdgeRatio = Math.Clamp(_options.SafeZoneEdgeRatio, 0, 0.49);
        var safeHalfSize = (0.5 - safeEdgeRatio) / clampedZoom;
        var visibilityEdgeRatio = Math.Clamp(
            _options.CursorVisibilityEdgeRatio,
            0,
            0.49);
        var visibilityHalfSize = (0.5 - visibilityEdgeRatio) / clampedZoom;
        var cursor = FindLastPositionAtOrBefore(ordered, startMs);
        var centerX = ClampCenter(cursor.X, viewportHalfSize);
        var centerY = ClampCenter(cursor.Y, viewportHalfSize);
        var targetX = centerX;
        var targetY = centerY;
        var velocityX = 0d;
        var velocityY = 0d;
        var densePoints = new List<CameraPoint>
        {
            new(startMs, centerX, centerY)
        };
        var mandatoryTimes = new HashSet<long> { startMs, endMs };
        var sampleIndex = 0;
        while (sampleIndex < ordered.Length && ordered[sampleIndex].TimeMs <= startMs)
        {
            sampleIndex++;
        }

        var simulationRate = Math.Clamp(Math.Max(60, frameRate), 30, 120);
        var currentTimeMs = startMs;
        var stepIndex = 1;
        while (currentTimeMs < endMs)
        {
            var nextTimeMs = Math.Min(
                endMs,
                startMs + (long)Math.Round(stepIndex * 1000d / simulationRate));
            stepIndex++;
            if (nextTimeMs <= currentTimeMs)
            {
                continue;
            }

            while (sampleIndex < ordered.Length &&
                   ordered[sampleIndex].TimeMs <= nextTimeMs)
            {
                var sample = ordered[sampleIndex++];
                AdvanceSpring(
                    (sample.TimeMs - currentTimeMs) / 1000d,
                    viewportHalfSize,
                    ref centerX,
                    ref centerY,
                    ref velocityX,
                    ref velocityY,
                    targetX,
                    targetY);
                currentTimeMs = Math.Max(currentTimeMs, sample.TimeMs);
                cursor = (sample.X, sample.Y);
                var previousCenterX = centerX;
                var previousCenterY = centerY;
                if (KeepCursorVisible(
                        cursor,
                        visibilityHalfSize,
                        viewportHalfSize,
                        ref centerX,
                        ref centerY,
                        ref velocityX,
                        ref velocityY))
                {
                    if (densePoints[^1].TimeMs < currentTimeMs)
                    {
                        var previousTimeMs = Math.Max(
                            densePoints[^1].TimeMs,
                            currentTimeMs - 1);
                        AddPoint(
                            densePoints,
                            previousTimeMs,
                            previousCenterX,
                            previousCenterY);
                        mandatoryTimes.Add(previousTimeMs);
                    }

                    AddPoint(densePoints, currentTimeMs, centerX, centerY);
                    mandatoryTimes.Add(currentTimeMs);
                }

                if (UpdateTarget(
                        cursor,
                        centerX,
                        centerY,
                        safeHalfSize,
                        viewportHalfSize,
                        ref targetX,
                        ref targetY))
                {
                    AddPoint(densePoints, currentTimeMs, centerX, centerY);
                    mandatoryTimes.Add(currentTimeMs);
                }
            }

            AdvanceSpring(
                (nextTimeMs - currentTimeMs) / 1000d,
                viewportHalfSize,
                ref centerX,
                ref centerY,
                ref velocityX,
                ref velocityY,
                targetX,
                targetY);
            currentTimeMs = nextTimeMs;
            AddPoint(densePoints, currentTimeMs, centerX, centerY);
            if (UpdateTarget(
                    cursor,
                    centerX,
                    centerY,
                    safeHalfSize,
                    viewportHalfSize,
                    ref targetX,
                    ref targetY))
            {
                mandatoryTimes.Add(currentTimeMs);
            }
        }

        return Simplify(
            densePoints,
            mandatoryTimes,
            _options.PathSimplificationTolerance / clampedZoom);
    }

    private void AdvanceSpring(
        double deltaSeconds,
        double viewportHalfSize,
        ref double centerX,
        ref double centerY,
        ref double velocityX,
        ref double velocityY,
        double targetX,
        double targetY)
    {
        if (deltaSeconds <= 0)
        {
            return;
        }

        var mass = Math.Max(0.01, _options.SpringMass);
        var stiffness = Math.Max(0, _options.SpringStiffness);
        var damping = Math.Max(0, _options.SpringDamping);
        var accelerationX =
            ((stiffness * (targetX - centerX)) - (damping * velocityX)) / mass;
        var accelerationY =
            ((stiffness * (targetY - centerY)) - (damping * velocityY)) / mass;
        velocityX += accelerationX * deltaSeconds;
        velocityY += accelerationY * deltaSeconds;
        centerX += velocityX * deltaSeconds;
        centerY += velocityY * deltaSeconds;
        ClampToViewport(ref centerX, ref velocityX, viewportHalfSize);
        ClampToViewport(ref centerY, ref velocityY, viewportHalfSize);
    }

    private static bool UpdateTarget(
        (double X, double Y) cursor,
        double centerX,
        double centerY,
        double safeHalfSize,
        double viewportHalfSize,
        ref double targetX,
        ref double targetY)
    {
        if (Math.Abs(cursor.X - centerX) <= safeHalfSize &&
            Math.Abs(cursor.Y - centerY) <= safeHalfSize)
        {
            return false;
        }

        var nextTargetX = ClampCenter(cursor.X, viewportHalfSize);
        var nextTargetY = ClampCenter(cursor.Y, viewportHalfSize);
        if (Math.Abs(nextTargetX - targetX) < 0.000001 &&
            Math.Abs(nextTargetY - targetY) < 0.000001)
        {
            return false;
        }

        targetX = nextTargetX;
        targetY = nextTargetY;
        return true;
    }

    private static bool KeepCursorVisible(
        (double X, double Y) cursor,
        double visibilityHalfSize,
        double viewportHalfSize,
        ref double centerX,
        ref double centerY,
        ref double velocityX,
        ref double velocityY)
    {
        var nextCenterX = KeepAxisVisible(
            cursor.X,
            centerX,
            visibilityHalfSize,
            viewportHalfSize);
        var nextCenterY = KeepAxisVisible(
            cursor.Y,
            centerY,
            visibilityHalfSize,
            viewportHalfSize);
        var changed = false;

        if (nextCenterX > centerX)
        {
            centerX = nextCenterX;
            velocityX = Math.Max(0, velocityX);
            changed = true;
        }
        else if (nextCenterX < centerX)
        {
            centerX = nextCenterX;
            velocityX = Math.Min(0, velocityX);
            changed = true;
        }

        if (nextCenterY > centerY)
        {
            centerY = nextCenterY;
            velocityY = Math.Max(0, velocityY);
            changed = true;
        }
        else if (nextCenterY < centerY)
        {
            centerY = nextCenterY;
            velocityY = Math.Min(0, velocityY);
            changed = true;
        }

        return changed;
    }

    private static double KeepAxisVisible(
        double cursor,
        double center,
        double visibilityHalfSize,
        double viewportHalfSize)
    {
        cursor = Math.Clamp(cursor, 0, 1);
        var minimumCenter = Math.Max(viewportHalfSize, cursor - visibilityHalfSize);
        var maximumCenter = Math.Min(1 - viewportHalfSize, cursor + visibilityHalfSize);
        if (minimumCenter > maximumCenter)
        {
            return ClampCenter(cursor, viewportHalfSize);
        }

        return Math.Clamp(center, minimumCenter, maximumCenter);
    }

    private static (double X, double Y) FindLastPositionAtOrBefore(
        IReadOnlyList<MouseSample> samples,
        long timeMs)
    {
        for (var index = samples.Count - 1; index >= 0; index--)
        {
            if (samples[index].TimeMs <= timeMs)
            {
                return (samples[index].X, samples[index].Y);
            }
        }

        return (0.5, 0.5);
    }

    private static double ClampCenter(double value, double viewportHalfSize)
        => Math.Clamp(value, viewportHalfSize, 1 - viewportHalfSize);

    private static void ClampToViewport(
        ref double position,
        ref double velocity,
        double viewportHalfSize)
    {
        var clamped = ClampCenter(position, viewportHalfSize);
        if ((clamped <= viewportHalfSize && velocity < 0) ||
            (clamped >= 1 - viewportHalfSize && velocity > 0))
        {
            velocity = 0;
        }

        position = clamped;
    }

    private static void AddPoint(
        List<CameraPoint> points,
        long timeMs,
        double x,
        double y)
    {
        var point = new CameraPoint(timeMs, x, y);
        if (points[^1].TimeMs == timeMs)
        {
            points[^1] = point;
        }
        else
        {
            points.Add(point);
        }
    }

    private static List<CameraPoint> Simplify(
        IReadOnlyList<CameraPoint> points,
        IReadOnlySet<long> mandatoryTimes,
        double tolerance)
    {
        if (points.Count <= 2)
        {
            return points.ToList();
        }

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;
        for (var index = 1; index < points.Count - 1; index++)
        {
            keep[index] = mandatoryTimes.Contains(points[index].TimeMs);
        }

        var anchors = Enumerable.Range(0, points.Count)
            .Where(index => keep[index])
            .ToArray();
        for (var anchorIndex = 1; anchorIndex < anchors.Length; anchorIndex++)
        {
            SimplifyRange(
                points,
                anchors[anchorIndex - 1],
                anchors[anchorIndex],
                Math.Max(0.000001, tolerance),
                keep);
        }

        return points
            .Where((_, index) => keep[index])
            .ToList();
    }

    private static void SimplifyRange(
        IReadOnlyList<CameraPoint> points,
        int startIndex,
        int endIndex,
        double tolerance,
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
                var expectedX = startPoint.X + ((endPoint.X - startPoint.X) * progress);
                var expectedY = startPoint.Y + ((endPoint.Y - startPoint.Y) * progress);
                var error = Distance(points[index].X, points[index].Y, expectedX, expectedY);
                if (error > maxError)
                {
                    maxError = error;
                    maxIndex = index;
                }
            }

            if (maxIndex < 0 || maxError <= tolerance)
            {
                continue;
            }

            keep[maxIndex] = true;
            ranges.Push((start, maxIndex));
            ranges.Push((maxIndex, end));
        }
    }

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
