using LensFlow.Core.Editing;
using LensFlow.Core.Models;

namespace LensFlow.Core.Tests;

public sealed class TimelineEditorTests
{
    [Fact]
    public void SplitVideoSegment_CreatesTwoAdjacentSegments()
    {
        var selected = new VideoSegment { StartMs = 0, EndMs = 5000 };
        IList<VideoSegment> segments = new List<VideoSegment> { selected };

        var changed = new TimelineEditor().SplitVideoSegment(segments, selected.Id, 2000);

        Assert.True(changed);
        Assert.Equal(2, segments.Count);
        Assert.Equal((0, 2000), (segments[0].StartMs, segments[0].EndMs));
        Assert.Equal((2000, 5000), (segments[1].StartMs, segments[1].EndMs));
    }

    [Fact]
    public void SplitCameraShot_PreservesMotionOnBothSides()
    {
        var selected = new CameraShot
        {
            StartMs = 1000,
            EndMs = 4000,
            Zoom = 1.8,
            Points =
            [
                new CameraPoint(1000, 0.2, 0.3),
                new CameraPoint(4000, 0.8, 0.7)
            ]
        };
        IList<CameraShot> shots = new List<CameraShot> { selected };

        var changed = new TimelineEditor().SplitCameraShot(shots, selected.Id, 2500);

        Assert.True(changed);
        Assert.Equal(2, shots.Count);
        Assert.Equal(2500, shots[0].EndMs);
        Assert.Equal(2500, shots[1].StartMs);
        Assert.Equal(shots[0].Points[^1], shots[1].Points[0]);
        Assert.Equal(0.5, shots[0].Points[^1].X, 3);
    }

    [Fact]
    public void ApplyCameraShotTiming_MovesRangeAndRebuildsPathFromDestinationMouseSamples()
    {
        var selected = new CameraShot
        {
            StartMs = 1000,
            EndMs = 3000,
            Zoom = 3,
            Points =
            [
                new CameraPoint(1000, 0.2, 0.3),
                new CameraPoint(3000, 0.8, 0.7)
            ]
        };
        IList<CameraShot> shots = new List<CameraShot> { selected };
        var editor = new TimelineEditor();
        var baseline = editor.CaptureCameraShotTiming(
            shots.ToArray(),
            selected.Id);
        var mouseSamples = new[]
        {
            new MouseSample(2000, 0.8, 0.2, MouseEventKind.Move),
            new MouseSample(2500, 0.8, 0.25, MouseEventKind.Move),
            new MouseSample(3000, 0.7, 0.3, MouseEventKind.Move),
            new MouseSample(3600, 0.4, 0.6, MouseEventKind.Move),
            new MouseSample(4300, 0.15, 0.8, MouseEventKind.Move),
            new MouseSample(5000, 0.1, 0.85, MouseEventKind.Move)
        };

        var changed = editor.ApplyCameraShotTiming(
            shots,
            Assert.IsType<CameraShotTimingState>(baseline),
            2500,
            4500,
            mouseSamples,
            30,
            6000);

        Assert.True(changed);
        Assert.Equal((2500, 4500), (selected.StartMs, selected.EndMs));
        Assert.Equal(2500, selected.Points[0].TimeMs);
        Assert.Equal(4500, selected.Points[^1].TimeMs);
        Assert.Equal(0.8, selected.Points[0].X, 3);
        Assert.True(selected.Points[^1].X < 0.6);
        Assert.True(selected.UserLocked);
    }

    [Fact]
    public void ApplyCameraShotTiming_ResizesRangeUsingMouseSamplesInNewRange()
    {
        var selected = new CameraShot
        {
            StartMs = 1000,
            EndMs = 4000,
            Zoom = 3,
            Points =
            [
                new CameraPoint(1000, 0.1, 0.2),
                new CameraPoint(2000, 0.3, 0.4),
                new CameraPoint(4000, 0.9, 0.8)
            ]
        };
        IList<CameraShot> shots = new List<CameraShot> { selected };
        var editor = new TimelineEditor();
        var baseline = editor.CaptureCameraShotTiming(
            shots.ToArray(),
            selected.Id);
        var mouseSamples = new[]
        {
            new MouseSample(2000, 0.9, 0.2, MouseEventKind.Move),
            new MouseSample(2500, 0.8, 0.3, MouseEventKind.Move),
            new MouseSample(3500, 0.5, 0.5, MouseEventKind.Move),
            new MouseSample(4500, 0.2, 0.7, MouseEventKind.Move),
            new MouseSample(5200, 0.1, 0.8, MouseEventKind.Move)
        };

        var changed = editor.ApplyCameraShotTiming(
            shots,
            Assert.IsType<CameraShotTimingState>(baseline),
            2500,
            5000,
            mouseSamples,
            30,
            6000);

        Assert.True(changed);
        Assert.Equal(2500, selected.Points[0].TimeMs);
        Assert.Equal(5000, selected.Points[^1].TimeMs);
        Assert.Equal(0.8, selected.Points[0].X, 3);
        Assert.True(selected.Points[^1].X < selected.Points[0].X);
    }

    [Fact]
    public void ApplyCameraShotTiming_RejectsOverlapAndShortRanges()
    {
        var selected = new CameraShot { StartMs = 1000, EndMs = 3000, Zoom = 3 };
        var neighbor = new CameraShot { StartMs = 4000, EndMs = 5000 };
        IList<CameraShot> shots = new List<CameraShot> { selected, neighbor };
        var editor = new TimelineEditor();
        var baseline = Assert.IsType<CameraShotTimingState>(
            editor.CaptureCameraShotTiming(shots.ToArray(), selected.Id));

        Assert.False(editor.ApplyCameraShotTiming(shots, baseline, 2500, 4500, [], 30, 6000));
        Assert.False(editor.ApplyCameraShotTiming(shots, baseline, 2900, 2950, [], 30, 6000));
        Assert.Equal((1000, 3000), (selected.StartMs, selected.EndMs));
    }

    [Fact]
    public void ApplyCameraShotTiming_RestoresBaselineAfterGestureReturnsToOrigin()
    {
        var selected = new CameraShot
        {
            StartMs = 1000,
            EndMs = 3000,
            UserLocked = false,
            Points =
            [
                new CameraPoint(1000, 0.2, 0.3),
                new CameraPoint(3000, 0.8, 0.7)
            ]
        };
        IList<CameraShot> shots = new List<CameraShot> { selected };
        var editor = new TimelineEditor();
        var baseline = Assert.IsType<CameraShotTimingState>(
            editor.CaptureCameraShotTiming(shots.ToArray(), selected.Id));

        var mouseSamples = new[]
        {
            new MouseSample(2000, 0.9, 0.8, MouseEventKind.Move),
            new MouseSample(4000, 0.1, 0.2, MouseEventKind.Move)
        };
        Assert.True(editor.ApplyCameraShotTiming(shots, baseline, 2000, 4000, mouseSamples, 30, 6000));
        Assert.True(editor.ApplyCameraShotTiming(shots, baseline, 1000, 3000, mouseSamples, 30, 6000));

        Assert.False(selected.UserLocked);
        Assert.Equal(baseline.Points, selected.Points);
    }

    [Fact]
    public void ApplyCameraShotTiming_UsesLastKnownPositionWhenMouseIsStationary()
    {
        var selected = new CameraShot { StartMs = 1000, EndMs = 3000, Zoom = 3 };
        IList<CameraShot> shots = new List<CameraShot> { selected };
        var editor = new TimelineEditor();
        var baseline = Assert.IsType<CameraShotTimingState>(
            editor.CaptureCameraShotTiming(shots.ToArray(), selected.Id));
        var mouseSamples = new[]
        {
            new MouseSample(3500, 0.25, 0.75, MouseEventKind.Move),
            new MouseSample(6000, 0.25, 0.75, MouseEventKind.Move)
        };

        Assert.True(editor.ApplyCameraShotTiming(
            shots,
            baseline,
            4000,
            5000,
            mouseSamples,
            30,
            6000));

        Assert.Equal(2, selected.Points.Count);
        Assert.All(selected.Points, point =>
        {
            Assert.Equal(0.25, point.X, 3);
            Assert.Equal(0.75, point.Y, 3);
        });
    }

    [Fact]
    public void RebuildCameraShotPath_RespondsToChangedZoomSafeZone()
    {
        var shot = new CameraShot
        {
            StartMs = 1000,
            EndMs = 3000,
            Zoom = 1.2
        };
        var samples = new[]
        {
            new MouseSample(1000, 0.5, 0.5, MouseEventKind.Move),
            new MouseSample(1500, 0.65, 0.5, MouseEventKind.Move),
            new MouseSample(2500, 0.65, 0.5, MouseEventKind.Move)
        };
        var editor = new TimelineEditor();

        Assert.True(editor.RebuildCameraShotPath(
            shot,
            samples,
            30,
            markUserLocked: false));
        Assert.All(shot.Points, point => Assert.Equal(0.5, point.X, 6));
        Assert.False(shot.UserLocked);

        shot.Zoom = 3;
        Assert.True(editor.RebuildCameraShotPath(shot, samples, 30));
        Assert.Contains(shot.Points, point => point.X > 0.55);
        Assert.True(shot.UserLocked);
    }
}
