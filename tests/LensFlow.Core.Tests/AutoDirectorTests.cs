using LensFlow.Core.Editing;
using LensFlow.Core.Models;

namespace LensFlow.Core.Tests;

public sealed class AutoDirectorTests
{
    [Fact]
    public void Generate_MergesNearbyClicksAndBuildsFollowPoints()
    {
        var samples = new[]
        {
            new MouseSample(100, 0.1, 0.2, MouseEventKind.Move),
            new MouseSample(500, 0.2, 0.3, MouseEventKind.LeftClick),
            new MouseSample(750, 0.35, 0.4, MouseEventKind.Move),
            new MouseSample(1100, 0.7, 0.6, MouseEventKind.LeftClick),
            new MouseSample(1350, 0.8, 0.7, MouseEventKind.Move)
        };

        var shots = new AutoDirector().Generate(samples, 3000);

        var shot = Assert.Single(shots);
        Assert.Equal(300, shot.StartMs);
        Assert.Equal(2600, shot.EndMs);
        Assert.True(shot.Points.Count >= 3);
        Assert.Contains(shot.Points, point => point.X > 0.5);
    }

    [Fact]
    public void Generate_ReturnsNoShotsWithoutClicks()
    {
        var samples = new[]
        {
            new MouseSample(100, 0.1, 0.2, MouseEventKind.Move),
            new MouseSample(500, 0.2, 0.3, MouseEventKind.Move)
        };

        var shots = new AutoDirector().Generate(samples, 1000);

        Assert.Empty(shots);
    }
}
