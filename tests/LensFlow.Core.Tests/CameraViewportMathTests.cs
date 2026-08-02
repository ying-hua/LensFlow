using LensFlow.Core.Editing;
using LensFlow.Core.Models;

namespace LensFlow.Core.Tests;

public sealed class CameraViewportMathTests
{
    [Fact]
    public void Resolve_TranslatesFocusPointToViewportCenter()
    {
        var transform = CameraViewportMath.Resolve(new CameraFrame(0.75, 0.25, 2));

        Assert.Equal(2, transform.Zoom);
        Assert.Equal(-0.5, transform.TranslateXRatio, 6);
        Assert.Equal(0.5, transform.TranslateYRatio, 6);
        Assert.Equal(
            0.5,
            TransformPoint(0.75, transform.Zoom, transform.TranslateXRatio),
            6);
        Assert.Equal(
            0.5,
            TransformPoint(0.25, transform.Zoom, transform.TranslateYRatio),
            6);
    }

    [Fact]
    public void Resolve_ClampsViewportAtSourceEdges()
    {
        var transform = CameraViewportMath.Resolve(new CameraFrame(0.95, 0.05, 2));

        Assert.Equal(0.75, transform.CenterX, 6);
        Assert.Equal(0.25, transform.CenterY, 6);
        Assert.Equal(-0.5, transform.TranslateXRatio, 6);
        Assert.Equal(0.5, transform.TranslateYRatio, 6);
    }

    private static double TransformPoint(double point, double zoom, double translation)
        => 0.5 + (zoom * (point - 0.5)) + translation;
}
