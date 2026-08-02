using LensFlow.Core.Models;

namespace LensFlow.Core.Editing;

public static class CameraViewportMath
{
    public static CameraViewportTransform Resolve(CameraFrame frame)
    {
        var zoom = Math.Max(1, frame.Zoom);
        var viewportHalfSize = 0.5 / zoom;
        var centerX = Math.Clamp(
            frame.CenterX,
            viewportHalfSize,
            1 - viewportHalfSize);
        var centerY = Math.Clamp(
            frame.CenterY,
            viewportHalfSize,
            1 - viewportHalfSize);

        return new CameraViewportTransform(
            zoom,
            centerX,
            centerY,
            zoom * (0.5 - centerX),
            zoom * (0.5 - centerY));
    }
}

public readonly record struct CameraViewportTransform(
    double Zoom,
    double CenterX,
    double CenterY,
    double TranslateXRatio,
    double TranslateYRatio);
