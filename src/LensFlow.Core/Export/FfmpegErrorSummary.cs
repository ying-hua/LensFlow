namespace LensFlow.Core.Export;

public static class FfmpegErrorSummary
{
    private const int MaxLines = 6;
    private const int MaxLineLength = 300;

    /// <summary>
    /// Condenses FFmpeg's stderr into something a message box can render.
    /// </summary>
    /// <remarks>
    /// A filter error is reported once per frame and every copy echoes the
    /// whole filter expression, so stderr routinely reaches tens of kilobytes.
    /// Passing that straight to a message box produced a dialog the user never
    /// saw - only the alert sound played.
    /// </remarks>
    public static string Summarize(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return string.Empty;
        }

        var lines = error.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unique = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            var trimmed = line.Length > MaxLineLength
                ? string.Concat(line.AsSpan(0, MaxLineLength), "...")
                : line;
            if (seen.Add(trimmed))
            {
                unique.Add(trimmed);
            }
        }

        if (unique.Count == 0)
        {
            return string.Empty;
        }

        var shown = unique.Take(MaxLines).ToList();
        var omitted = lines.Length - shown.Count;
        if (omitted > 0)
        {
            shown.Add($"(另有 {omitted} 行相似的 FFmpeg 输出已省略)");
        }

        return string.Join(Environment.NewLine, shown);
    }
}
