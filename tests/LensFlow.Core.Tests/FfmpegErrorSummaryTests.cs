using LensFlow.Core.Export;

namespace LensFlow.Core.Tests;

public sealed class FfmpegErrorSummaryTests
{
    [Fact]
    public void Summarize_CollapsesRepeatedFfmpegOutput()
    {
        var error = string.Join('\n', Enumerable.Repeat("[Eval] Missing ')' or too many args", 400));

        var summary = FfmpegErrorSummary.Summarize(error);

        Assert.Contains("Missing ')' or too many args", summary);
        Assert.Contains("已省略", summary);
        Assert.True(summary.Length < 1000, $"summary was {summary.Length} chars");
    }

    [Fact]
    public void Summarize_TruncatesLinesThatEchoTheWholeFilterExpression()
    {
        var error = "[Eval] Missing ')' in '" + new string('x', 20_000) + "'";

        var summary = FfmpegErrorSummary.Summarize(error);

        Assert.StartsWith("[Eval] Missing ')' in '", summary);
        Assert.EndsWith("...", summary);
        Assert.True(summary.Length < 400, $"summary was {summary.Length} chars");
    }

    [Fact]
    public void Summarize_KeepsDistinctLines()
    {
        var summary = FfmpegErrorSummary.Summarize("first problem\nsecond problem\nfirst problem");

        Assert.Contains("first problem", summary);
        Assert.Contains("second problem", summary);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    public void Summarize_ReturnsEmptyWhenThereIsNothingToShow(string? error)
    {
        Assert.Equal(string.Empty, FfmpegErrorSummary.Summarize(error));
    }
}
