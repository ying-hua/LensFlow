using LensFlow.Core.Editing;
using LensFlow.Core.Models;

namespace LensFlow.Core.Tests;

public sealed class CameraEvaluatorTests
{
    [Fact]
    public void Evaluate_UsesZoomEnvelopeAndInterpolatedCenter()
    {
        var shot = new CameraShot
        {
            StartMs = 1000,
            EndMs = 3000,
            Zoom = 2,
            Points =
            [
                new CameraPoint(1000, 0.2, 0.2),
                new CameraPoint(2000, 0.8, 0.6),
                new CameraPoint(3000, 0.8, 0.6)
            ]
        };

        var frame = new CameraEvaluator().Evaluate([shot], 2000);

        Assert.InRange(frame.Zoom, 1.99, 2.05);
        Assert.Equal(0.8, frame.CenterX, 3);
        Assert.Equal(0.6, frame.CenterY, 3);
    }

    [Fact]
    public void Evaluate_ReturnsWideOutsideShots()
    {
        var frame = new CameraEvaluator().Evaluate([], 500);

        Assert.Equal(CameraFrame.Wide, frame);
    }
}
