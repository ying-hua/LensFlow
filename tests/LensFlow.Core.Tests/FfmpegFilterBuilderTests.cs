using LensFlow.Core.Export;
using LensFlow.Core.Models;

namespace LensFlow.Core.Tests;

public sealed class FfmpegFilterBuilderTests
{
    [Fact]
    public void BuildVideoFilter_UsesTrimRelativeCameraTimes()
    {
        var project = LensFlowProject.Create("test", "C:\\temp", 1920, 1080, 30);
        project.DurationMs = 10_000;
        project.Edit.TrimStartMs = 2_000;
        project.Edit.TrimEndMs = 8_000;
        project.CameraShots =
        [
            new CameraShot
            {
                StartMs = 3_000,
                EndMs = 5_000,
                Zoom = 1.7,
                Points =
                [
                    new CameraPoint(3_000, 0.25, 0.4),
                    new CameraPoint(5_000, 0.7, 0.6)
                ]
            }
        ];

        var filter = new FfmpegFilterBuilder().BuildVideoFilter(project);

        Assert.Contains("trim=start=0:end=6", filter);
        Assert.Contains("between(in_time,1,3)", filter);
        Assert.Contains("between(in_time,3,3.4)", filter);
        Assert.Contains("zoompan", filter);
        Assert.Contains("s=1920x1080", filter);
    }

    [Fact]
    public void BuildVideoFilter_DoesNotZoomOutBetweenTouchingShots()
    {
        var project = LensFlowProject.Create("test", "C:\\temp", 1920, 1080, 30);
        project.DurationMs = 5000;
        project.Edit.TrimEndMs = 5000;
        project.CameraShots =
        [
            new CameraShot { StartMs = 1000, EndMs = 2000, Zoom = 2 },
            new CameraShot { StartMs = 2000, EndMs = 3000, Zoom = 2 }
        ];

        var filter = new FfmpegFilterBuilder().BuildVideoFilter(project);

        Assert.DoesNotContain("between(in_time,2,2.4)", filter);
        Assert.Contains("between(in_time,3,3.4)", filter);
    }

    [Fact]
    public void BuildVideoFilter_AddsCanvasForSquareOutput()
    {
        var project = LensFlowProject.Create("test", "C:\\temp", 1920, 1080, 30);
        project.DurationMs = 1000;
        project.Edit.TrimEndMs = 1000;
        project.Canvas.AspectRatio = AspectRatioPreset.Square;

        var filter = new FfmpegFilterBuilder().BuildVideoFilter(project);

        Assert.Contains("scale=1080:1080:force_original_aspect_ratio=decrease", filter);
        Assert.Contains("pad=1080:1080", filter);
    }
}
