using LensFlow.Core.Models;
using LensFlow.Core.Persistence;

namespace LensFlow.Core.Tests;

public sealed class ProjectRepositoryTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsProjectState()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "LensFlowTests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var project = LensFlowProject.Create("roundtrip", directory, 1280, 720, 30);
            project.DurationMs = 2500;
            project.Edit.TrimEndMs = 2000;
            project.MouseSamples.Add(new MouseSample(400, 0.2, 0.3, MouseEventKind.LeftClick));
            project.CameraShots.Add(new CameraShot
            {
                StartMs = 200,
                EndMs = 1900,
                Points = [new CameraPoint(400, 0.2, 0.3)]
            });

            var repository = new ProjectRepository();
            await repository.SaveAsync(project);
            var loaded = await repository.LoadAsync(directory);

            Assert.Equal(project.Id, loaded.Id);
            Assert.Equal(2500, loaded.DurationMs);
            Assert.Single(loaded.MouseSamples);
            Assert.Single(loaded.CameraShots);
            Assert.True(File.Exists(Path.Combine(directory, "project.json")));
            Assert.True(File.Exists(Path.Combine(directory, "project.db")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
