using LensFlow.Core.Editing;
using LensFlow.Core.Models;

namespace LensFlow.Core.Tests;

public sealed class CameraPathBuilderTests
{
    [Fact]
    public void Build_DoesNotMoveBeforeMouseLeavesSafeZone()
    {
        var samples = new[]
        {
            new MouseSample(1000, 0.25, 0.5, MouseEventKind.Move),
            new MouseSample(2000, 0.75, 0.5, MouseEventKind.Move),
            new MouseSample(2500, 0.75, 0.5, MouseEventKind.Move)
        };

        var points = new CameraPathBuilder().Build(samples, 1000, 3000, 3, 30);

        Assert.All(
            points.Where(point => point.TimeMs < 2000),
            point => Assert.Equal(0.25, point.X, 6));
        Assert.Contains(points, point => point.TimeMs > 2000 && point.X > 0.25);
    }

    [Fact]
    public void Build_TriggersMoreReadilyAtHighZoom()
    {
        var samples = new[]
        {
            new MouseSample(1000, 0.5, 0.5, MouseEventKind.Move),
            new MouseSample(1500, 0.65, 0.5, MouseEventKind.Move),
            new MouseSample(2500, 0.65, 0.5, MouseEventKind.Move)
        };
        var builder = new CameraPathBuilder();

        var lowZoom = builder.Build(samples, 1000, 3000, 1.2, 30);
        var highZoom = builder.Build(samples, 1000, 3000, 3, 30);

        Assert.All(lowZoom, point => Assert.Equal(0.5, point.X, 6));
        Assert.Contains(highZoom, point => point.X > 0.55);
    }

    [Fact]
    public void Build_UsesThirtyPercentEdgeTriggerZone()
    {
        var insideSamples = new[]
        {
            new MouseSample(1000, 0.5, 0.5, MouseEventKind.Move),
            new MouseSample(1500, 0.595, 0.5, MouseEventKind.Move)
        };
        var edgeSamples = new[]
        {
            new MouseSample(1000, 0.5, 0.5, MouseEventKind.Move),
            new MouseSample(1500, 0.605, 0.5, MouseEventKind.Move)
        };
        var builder = new CameraPathBuilder();

        var inside = builder.Build(insideSamples, 1000, 3000, 2, 30);
        var edge = builder.Build(edgeSamples, 1000, 3000, 2, 30);

        Assert.All(inside, point => Assert.Equal(0.5, point.X, 6));
        Assert.Contains(edge, point => point.X > 0.5);
    }

    [Fact]
    public void Build_UnderdampedSpringCanOvershootExpectedCenter()
    {
        var samples = new[]
        {
            new MouseSample(1000, 0.5, 0.5, MouseEventKind.Move),
            new MouseSample(1500, 0.65, 0.5, MouseEventKind.Move),
            new MouseSample(3500, 0.65, 0.5, MouseEventKind.Move)
        };

        var points = new CameraPathBuilder().Build(samples, 1000, 4000, 3, 30);

        Assert.True(points.Max(point => point.X) > 0.65);
        Assert.InRange(points[^1].X, 0.64, 0.66);
    }

    [Fact]
    public void Build_KeepsFastMovingCursorInsideViewport()
    {
        var samples = new[]
        {
            new MouseSample(1000, 0.25, 0.5, MouseEventKind.Move),
            new MouseSample(2000, 0.8, 0.5, MouseEventKind.Move)
        };

        var points = new CameraPathBuilder().Build(samples, 1000, 3000, 3, 30);
        var centerAtMouseMove = points.Single(point => point.TimeMs == 2000);
        var visibilityHalfSize =
            (0.5 - CameraMotionDefaults.CursorVisibilityEdgeRatio) / 3;

        Assert.InRange(
            Math.Abs(0.8 - centerAtMouseMove.X),
            0,
            visibilityHalfSize + 0.000001);
    }

    [Fact]
    public void Build_PreservesOrderedPathForSimultaneousMouseEvents()
    {
        var samples = new[]
        {
            new MouseSample(1000, 0.25, 0.5, MouseEventKind.Move),
            new MouseSample(2000, 0.7, 0.5, MouseEventKind.Move),
            new MouseSample(2000, 0.8, 0.5, MouseEventKind.LeftClick)
        };

        var points = new CameraPathBuilder().Build(samples, 1000, 3000, 3, 30);

        Assert.True(points.Zip(points.Skip(1)).All(pair =>
            pair.First.TimeMs < pair.Second.TimeMs));
    }
}
