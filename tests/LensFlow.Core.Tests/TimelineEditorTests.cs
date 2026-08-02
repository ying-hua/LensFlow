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
}
