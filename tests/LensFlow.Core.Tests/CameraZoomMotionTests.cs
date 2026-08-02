using LensFlow.Core.Editing;
using LensFlow.Core.Models;

namespace LensFlow.Core.Tests;

public sealed class CameraZoomMotionTests
{
    [Fact]
    public void BuildPoints_UsesUnderdampedSpringForZoomInAndOut()
    {
        var shot = new CameraShot
        {
            StartMs = 1000,
            EndMs = 3500,
            Zoom = 2
        };

        var points = CameraZoomMotion.BuildPoints(shot, 30);

        Assert.Equal(1, points[0].Zoom);
        Assert.True(points.Max(point => point.Zoom) > 2);
        Assert.All(points, point => Assert.True(point.Zoom >= 1));
        Assert.Equal(1, points[^1].Zoom);
    }
}
