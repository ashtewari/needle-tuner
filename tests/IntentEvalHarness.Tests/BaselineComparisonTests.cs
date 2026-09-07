using Xunit;

namespace IntentEvalHarness.Tests;

public sealed class BaselineComparisonTests
{
    [Fact]
    public void BuildProviderComparison_ComputesMetricDeltas()
    {
        var current = new ProviderSummaryReport
        {
            DisplayName = "Needle Base",
            ErrorCases = 1,
            FallbackCases = 2,
            IntentAccuracy = 0.90,
            ParameterAccuracy = 0.80,
            AverageConfidence = 0.70,
            AverageDurationMs = 120,
            AverageCostUsd = 0.0m,
            TotalCostUsd = 0.0m,
            PerIntent = new Dictionary<string, IntentBreakdownSummary>(StringComparer.OrdinalIgnoreCase)
            {
                ["SEARCH_ITEM"] = new IntentBreakdownSummary
                {
                    IntentAccuracy = 0.95,
                    ParameterAccuracy = 0.85
                }
            }
        };

        var baseline = new ProviderSummaryReport
        {
            DisplayName = "OpenAI",
            ErrorCases = 0,
            FallbackCases = 0,
            IntentAccuracy = 0.93,
            ParameterAccuracy = 0.71,
            AverageConfidence = 0.82,
            AverageDurationMs = 200,
            AverageCostUsd = 0.01m,
            TotalCostUsd = 0.45m,
            PerIntent = new Dictionary<string, IntentBreakdownSummary>(StringComparer.OrdinalIgnoreCase)
            {
                ["SEARCH_ITEM"] = new IntentBreakdownSummary
                {
                    IntentAccuracy = 0.90,
                    ParameterAccuracy = 0.70
                }
            }
        };

        var comparison = Program.BuildProviderComparison(
            "needleBase",
            current,
            "openAi",
            baseline,
            "OpenAI",
            BaselineComparisonMetrics.Runtime | BaselineComparisonMetrics.Cost);

        Assert.NotNull(comparison.IntentAccuracy.Delta);
        Assert.NotNull(comparison.ParameterAccuracy.Delta);
        Assert.InRange(comparison.IntentAccuracy.Delta!.Value, -0.031, -0.029);
        Assert.InRange(comparison.ParameterAccuracy.Delta!.Value, 0.089, 0.091);
        Assert.NotNull(comparison.AverageDurationMs);
        Assert.NotNull(comparison.AverageCostUsd);
        Assert.True(comparison.PerIntent.ContainsKey("SEARCH_ITEM"));
    }
}
