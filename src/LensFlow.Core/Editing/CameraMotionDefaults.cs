namespace LensFlow.Core.Editing;

public static class CameraMotionDefaults
{
    public const double SafeZoneEdgeRatio = 0.30;
    public const double CursorVisibilityEdgeRatio = 0.08;
    public const double SpringMass = 1;
    public const double SpringStiffness = 90;
    public const double SpringDamping = 12;
    public const double PathSimplificationTolerance = 0.001;
    public const long ZoomInDurationMs = 350;
    public const long ZoomOutDurationMs = 400;
}
