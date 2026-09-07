using System.Text.Json;
using Xunit;

namespace IntentEvalHarness.Tests;

public sealed class ParameterComparisonTests
{
    [Fact]
    public void BuildOutcome_MatchesParametersAcrossJsonElementAndPrimitiveValues()
    {
        var testCase = new IntentEvalCase
        {
            Input = "move hammer",
            ExpectedIntent = "UPDATE_ITEM",
            ExpectedParameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["itemName"] = "hammer",
                ["quantity"] = 2
            }
        };

        var parsedParameters = JsonSerializer.Deserialize<Dictionary<string, object>>(
            """{"itemName":"Hammer","quantity":2}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var result = new IntentEvaluationResult
        {
            Intent = "UPDATE_ITEM",
            Parameters = new Dictionary<string, object>(parsedParameters, StringComparer.OrdinalIgnoreCase)
        };

        var outcome = Program.BuildOutcome(testCase, result);

        Assert.True(outcome.IntentMatch);
        Assert.True(outcome.ParamMatch);
    }

    [Fact]
    public void BuildOutcome_DetectsParameterMismatch()
    {
        var testCase = new IntentEvalCase
        {
            Input = "add hammer",
            ExpectedIntent = "ADD_ITEM",
            ExpectedParameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["itemName"] = "hammer",
                ["quantity"] = 2
            }
        };

        var result = new IntentEvaluationResult
        {
            Intent = "ADD_ITEM",
            Parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["itemName"] = "hammer",
                ["quantity"] = 5
            }
        };

        var outcome = Program.BuildOutcome(testCase, result);
        Assert.False(outcome.ParamMatch);
    }
}
