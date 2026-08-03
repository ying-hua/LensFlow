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
    public void BuildVideoFilter_UsesShortShotDurationForZoomIn()
    {
        var project = LensFlowProject.Create("test", "C:\\temp", 1920, 1080, 30);
        project.DurationMs = 2000;
        project.Edit.TrimEndMs = 2000;
        project.CameraShots =
        [
            new CameraShot { StartMs = 1000, EndMs = 1100, Zoom = 2 }
        ];

        var filter = new FfmpegFilterBuilder().BuildVideoFilter(project);

        Assert.Contains("(in_time-(1))/0.1", filter);
    }

    [Fact]
    public void BuildVideoFilter_InterpolatesCenterAtTrimStart()
    {
        var project = LensFlowProject.Create("test", "C:\\temp", 1920, 1080, 30);
        project.DurationMs = 2000;
        project.Edit.TrimStartMs = 500;
        project.Edit.TrimEndMs = 2000;
        project.CameraShots =
        [
            new CameraShot
            {
                StartMs = 0,
                EndMs = 1500,
                Points =
                [
                    new CameraPoint(0, 0.2, 0.4),
                    new CameraPoint(1000, 0.8, 0.6)
                ]
            }
        ];

        var filter = new FfmpegFilterBuilder().BuildVideoFilter(project);

        Assert.Contains(
            "0.2+(0.6)*clip((in_time-(-0.5))/1,0,1)",
            filter);
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

    [Theory]
    [InlineData(2)]
    [InlineData(150)]
    [InlineData(2000)]
    public void BuildVideoFilter_KeepsNestingWithinFfmpegParserLimit(int pointCount)
    {
        // FFmpeg's expression parser (av_expr_parse) gives up past roughly 90
        // nested levels with "Missing ')' or too many args". A camera path
        // holds one point per frame, so a chain that nested once per point
        // broke every export of a recording longer than a few seconds.
        var project = BuildProjectWithCameraPath(pointCount);

        var filter = new FfmpegFilterBuilder().BuildVideoFilter(project);

        Assert.True(
            MaxNestingDepth(filter) < 90,
            $"nesting depth {MaxNestingDepth(filter)} would be rejected by FFmpeg");
    }

    [Fact]
    public void BuildVideoFilter_CameraPathNestingGrowsOnlyLogarithmically()
    {
        // A 300x larger camera path must not cost 300x the parser depth, which
        // is the whole point of folding the ramp sum into a balanced tree.
        var few = MaxNestingDepth(new FfmpegFilterBuilder().BuildVideoFilter(BuildProjectWithCameraPath(5)));
        var many = MaxNestingDepth(new FfmpegFilterBuilder().BuildVideoFilter(BuildProjectWithCameraPath(1500)));

        Assert.True(many - few <= 12, $"depth grew from {few} to {many}");
    }

    [Fact]
    public void BuildVideoFilter_RampsAccumulateToTheLastCameraPoint()
    {
        // The ramp sum has to settle on the final point once every clip() is
        // saturated, which is what the old nested form used as its fallback.
        var project = LensFlowProject.Create("test", "C:\\temp", 1920, 1080, 30);
        project.DurationMs = 3000;
        project.Edit.TrimEndMs = 3000;
        project.CameraShots =
        [
            new CameraShot
            {
                StartMs = 0,
                EndMs = 3000,
                Zoom = 2,
                Points =
                [
                    new CameraPoint(0, 0.2, 0.1),
                    new CameraPoint(1000, 0.5, 0.4),
                    new CameraPoint(2000, 0.9, 0.6)
                ]
            }
        ];

        var filter = new FfmpegFilterBuilder().BuildVideoFilter(project);

        Assert.Contains(
            "((0.2+(0.3)*clip((in_time-(0))/1,0,1))+(0.4)*clip((in_time-(1))/1,0,1))",
            filter);
        Assert.DoesNotContain("if(lt(in_time", filter);
    }

    private static LensFlowProject BuildProjectWithCameraPath(int pointCount)
    {
        var project = LensFlowProject.Create("test", "C:\\temp", 1920, 1080, 30);
        project.DurationMs = pointCount * 33L;
        project.Edit.TrimEndMs = project.DurationMs;
        project.CameraShots =
        [
            new CameraShot
            {
                StartMs = 0,
                EndMs = project.DurationMs,
                Zoom = 1.6,
                Points = Enumerable.Range(0, pointCount)
                    .Select(index => new CameraPoint(
                        index * 33L,
                        0.3 + (index % 20) * 0.01,
                        0.4 + (index % 15) * 0.01))
                    .ToList()
            }
        ];

        return project;
    }

    private static int MaxNestingDepth(string expression)
    {
        var depth = 0;
        var max = 0;
        foreach (var character in expression)
        {
            if (character == '(')
            {
                max = Math.Max(max, ++depth);
            }
            else if (character == ')')
            {
                depth--;
            }
        }

        return max;
    }
}
