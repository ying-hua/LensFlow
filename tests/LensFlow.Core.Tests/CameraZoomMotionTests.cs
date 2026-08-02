using LensFlow.Core.Editing;
using LensFlow.Core.Models;

namespace LensFlow.Core.Tests;

public sealed class CameraZoomMotionTests
{
    [Fact]
    public void BuildPoints_HoldsTargetThroughShotAndExitsAfterEnd()
    {
        var shot = new CameraShot
        {
            StartMs = 1000,
            EndMs = 3500,
            Zoom = 2
        };

        var points = CameraZoomMotion.BuildPoints(shot, 30);

        Assert.Equal(1, points[0].Zoom);
        Assert.Contains(points, point => point.TimeMs == shot.EndMs && point.Zoom == 2);
        Assert.DoesNotContain(
            points.Where(point => point.TimeMs <= shot.EndMs),
            point => point.Zoom < 1 || point.Zoom > 2);
        Assert.Equal(
            shot.EndMs + CameraMotionDefaults.ZoomOutDurationMs,
            points[^1].TimeMs);
        Assert.Equal(1, points[^1].Zoom);
    }
}
