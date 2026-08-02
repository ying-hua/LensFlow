using System.Text.Json;
using LensFlow.Core.Models;
using Microsoft.Data.Sqlite;

namespace LensFlow.Core.Persistence;

public sealed class ProjectRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task SaveAsync(LensFlowProject project, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project.DirectoryPath);
        Directory.CreateDirectory(project.DirectoryPath);
        Directory.CreateDirectory(Path.Combine(project.DirectoryPath, "media"));
        Directory.CreateDirectory(Path.Combine(project.DirectoryPath, "events"));
        Directory.CreateDirectory(Path.Combine(project.DirectoryPath, "cache"));

        var json = JsonSerializer.Serialize(project, JsonOptions);
        await SaveDatabaseAsync(project.DatabasePath, json, cancellationToken);

        var manifestPath = Path.Combine(project.DirectoryPath, "project.json");
        var temporaryPath = manifestPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, manifestPath, true);
    }

    public async Task<LensFlowProject> LoadAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        var databasePath = Path.Combine(projectDirectory, "project.db");
        string json;

        if (File.Exists(databasePath))
        {
            await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT json FROM project_state WHERE id = 1;";
            json = (string?)await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidDataException("The project database does not contain project state.");
        }
        else
        {
            var manifestPath = Path.Combine(projectDirectory, "project.json");
            json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        }

        var project = JsonSerializer.Deserialize<LensFlowProject>(json, JsonOptions)
            ?? throw new InvalidDataException("The project state is invalid.");
        project.DirectoryPath = projectDirectory;

        if (project.SchemaVersion > LensFlowProject.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Project schema {project.SchemaVersion} is newer than this application supports.");
        }

        return project;
    }

    private static async Task SaveDatabaseAsync(
        string databasePath,
        string json,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken);

        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;";
            await pragma.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS project_state (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    json TEXT NOT NULL,
                    updated_utc TEXT NOT NULL
                );
                """;
            await schema.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO project_state (id, json, updated_utc)
            VALUES (1, $json, $updated)
            ON CONFLICT(id) DO UPDATE SET
                json = excluded.json,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$json", json);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
