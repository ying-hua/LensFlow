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

        Assert.Equal(2, frame.Zoom, 3);
        Assert.Equal(0.8, frame.CenterX, 3);
        Assert.Equal(0.6, frame.CenterY, 3);
    }

    [Fact]
    public void Evaluate_ReturnsWideOutsideShots()
    {
        var frame = new CameraEvaluator().Evaluate([], 500);

        Assert.Equal(CameraFrame.Wide, frame);
    }

    [Fact]
    public void Evaluate_StartsZoomOutAfterShotEnds()
    {
        var shot = new CameraShot
        {
            StartMs = 1000,
            EndMs = 2000,
            Zoom = 2,
            Points = [new CameraPoint(1000, 0.25, 0.75)]
        };
        var evaluator = new CameraEvaluator();

        Assert.Equal(1, evaluator.Evaluate([shot], 1000).Zoom, 3);
        Assert.Equal(2, evaluator.Evaluate([shot], 2000).Zoom, 3);
        Assert.Equal(1.5, evaluator.Evaluate([shot], 2200).Zoom, 3);
        Assert.Equal(1, evaluator.Evaluate([shot], 2400).Zoom, 3);
        Assert.Equal(CameraFrame.Wide, evaluator.Evaluate([shot], 2401));
    }

    [Fact]
    public void Evaluate_KeepsZoomForTouchingShots()
    {
        var shots = new[]
        {
            new CameraShot
            {
                StartMs = 1000,
                EndMs = 2000,
                Zoom = 2,
                Points = [new CameraPoint(1000, 0.25, 0.25)]
            },
            new CameraShot
            {
                StartMs = 2000,
                EndMs = 3000,
                Zoom = 2,
                Points = [new CameraPoint(2000, 0.75, 0.75)]
            }
        };
        var evaluator = new CameraEvaluator();

        Assert.Equal(2, evaluator.Evaluate(shots, 2000).Zoom, 3);
        Assert.Equal(2, evaluator.Evaluate(shots, 2175).Zoom, 3);
        Assert.Equal(2, evaluator.Evaluate(shots, 3000).Zoom, 3);
        Assert.Equal(1.5, evaluator.Evaluate(shots, 3200).Zoom, 3);
    }
}
