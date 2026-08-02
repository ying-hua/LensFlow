using System.Text.Json.Serialization;

namespace LensFlow.Core.Models;

public sealed class LensFlowProject
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled";
    public string DirectoryPath { get; set; } = string.Empty;
    public string MediaFileName { get; set; } = "source.mp4";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public long DurationMs { get; set; }
    public int SourceWidth { get; set; } = 1920;
    public int SourceHeight { get; set; } = 1080;
    public int FrameRate { get; set; } = 30;
    public EditState Edit { get; set; } = new();
    public CanvasSettings Canvas { get; set; } = new();
    public List<MouseSample> MouseSamples { get; set; } = [];
    public List<VideoSegment> VideoSegments { get; set; } = [];
    public List<CameraShot> CameraShots { get; set; } = [];

    [JsonIgnore]
    public string MediaPath => Path.Combine(DirectoryPath, "media", MediaFileName);

    [JsonIgnore]
    public string DatabasePath => Path.Combine(DirectoryPath, "project.db");

    public static LensFlowProject Create(
        string name,
        string directoryPath,
        int width,
        int height,
        int frameRate)
    {
        return new LensFlowProject
        {
            Name = name,
            DirectoryPath = directoryPath,
            SourceWidth = width,
            SourceHeight = height,
            FrameRate = frameRate
        };
    }
}

public sealed class EditState
{
    public long TrimStartMs { get; set; }
    public long TrimEndMs { get; set; }
}

public sealed class CanvasSettings
{
    public AspectRatioPreset AspectRatio { get; set; } = AspectRatioPreset.Original;
}

public enum AspectRatioPreset
{
    Original = 0,
    Landscape16By9 = 1,
    Square = 2,
    Standard4By3 = 3,
    Portrait9By16 = 4
}

public sealed class VideoSegment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long StartMs { get; set; }
    public long EndMs { get; set; }
}

public enum MouseEventKind
{
    Move = 0,
    LeftClick = 1,
    RightClick = 2
}

public sealed record MouseSample(long TimeMs, double X, double Y, MouseEventKind Kind)
{
    public double X { get; init; } = Math.Clamp(X, 0, 1);
    public double Y { get; init; } = Math.Clamp(Y, 0, 1);
}

public sealed class CameraShot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public double Zoom { get; set; } = 1.6;
    public bool UserLocked { get; set; }
    public List<CameraPoint> Points { get; set; } = [];
}

public sealed record CameraPoint(long TimeMs, double X, double Y)
{
    public double X { get; init; } = Math.Clamp(X, 0, 1);
    public double Y { get; init; } = Math.Clamp(Y, 0, 1);
}

public readonly record struct CameraFrame(double CenterX, double CenterY, double Zoom)
{
    public static CameraFrame Wide => new(0.5, 0.5, 1);
}
