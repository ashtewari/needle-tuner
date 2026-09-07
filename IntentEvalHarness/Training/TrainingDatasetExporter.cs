using System.Text;
using System.Text.Json;

namespace IntentEvalHarness;

public sealed class TrainingSeedCase
{
    public string Input { get; init; } = string.Empty;
    public string ExpectedIntent { get; init; } = string.Empty;
    public Dictionary<string, object> ExpectedParameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string Difficulty { get; init; } = "medium";
    public string? Notes { get; init; }
}

public sealed class TrainingDatasetExportResult
{
    public string JsonlPath { get; init; } = string.Empty;
    public string SummaryPath { get; init; } = string.Empty;
    public int TotalExamples { get; init; }
}

public static class TrainingDatasetExporter
{
    private static readonly JsonSerializerOptions SourceOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions JsonlOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static TrainingDatasetExportResult ExportJsonl(string projectRoot, string sourcePath, string outputPath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new ArgumentException($"Training seed dataset was not found: {sourcePath}");
        }

        var examples = JsonSerializer.Deserialize<List<TrainingSeedCase>>(File.ReadAllText(sourcePath), SourceOptions) ?? [];
        if (examples.Count == 0)
        {
            throw new ArgumentException($"Training seed dataset at '{sourcePath}' is empty.");
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        using var toolsDocument = JsonDocument.Parse(NeedleIntentClient.GetToolsJson());
        var tools = toolsDocument.RootElement.Clone();

        using (var writer = new StreamWriter(outputPath, false, new UTF8Encoding(false)))
        {
            foreach (var example in examples)
            {
                var answers = BuildAnswers(example);
                var row = new TrainingJsonlRow
                {
                    Query = example.Input,
                    Tools = tools,
                    Answers = answers,
                    Reasoning = string.IsNullOrWhiteSpace(example.Notes) ? null : example.Notes
                };

                writer.WriteLine(JsonSerializer.Serialize(row, JsonlOptions));
            }
        }

        var summaryPath = Path.Combine(
            Path.GetDirectoryName(outputPath) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(outputPath)}.summary.json");

        var relativeSourcePath = HarnessPathUtils.ToProjectRelative(projectRoot, sourcePath);
        var relativeJsonlPath = HarnessPathUtils.ToProjectRelative(projectRoot, outputPath);
        var relativeSummaryPath = HarnessPathUtils.ToProjectRelative(projectRoot, summaryPath);

        var summary = new
        {
            sourcePath = relativeSourcePath,
            jsonlPath = relativeJsonlPath,
            summaryPath = relativeSummaryPath,
            totalExamples = examples.Count,
            abstainExamples = examples.Count(example => string.Equals(example.ExpectedIntent, "UNCLEAR", StringComparison.OrdinalIgnoreCase)),
            intentCounts = examples
                .GroupBy(example => example.ExpectedIntent, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            difficultyCounts = examples
                .GroupBy(example => example.Difficulty, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase)
        };

        File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        return new TrainingDatasetExportResult
        {
            JsonlPath = relativeJsonlPath,
            SummaryPath = relativeSummaryPath,
            TotalExamples = examples.Count
        };
    }

    private static List<ToolAnswer> BuildAnswers(TrainingSeedCase example)
    {
        var toolName = MapIntentToToolName(example.ExpectedIntent);
        if (toolName is null)
        {
            return [];
        }

        return
        [
            new ToolAnswer
            {
                Name = toolName,
                Arguments = new Dictionary<string, object>(example.ExpectedParameters, StringComparer.OrdinalIgnoreCase)
            }
        ];
    }

    internal static string? MapIntentToToolName(string intent)
    {
        return intent.Trim().ToUpperInvariant() switch
        {
            "SEARCH_ITEM" => "search_item",
            "ADD_ITEM" => "add_item",
            "UPDATE_ITEM" => "update_item",
            "DELETE_ITEM" => "delete_item",
            "MANAGE_BOX" => "manage_box",
            "VIEW_INVENTORY" => "view_inventory",
            "UPLOAD_PHOTO" => "upload_photo",
            "GENERAL_HELP" => "general_help",
            "UNCLEAR" => null,
            _ => throw new ArgumentException($"Unsupported training intent '{intent}'.")
        };
    }

    private sealed class TrainingJsonlRow
    {
        public string Query { get; init; } = string.Empty;
        public JsonElement Tools { get; init; }
        public List<ToolAnswer> Answers { get; init; } = [];
        public string? Reasoning { get; init; }
    }

    private sealed class ToolAnswer
    {
        public string Name { get; init; } = string.Empty;
        public Dictionary<string, object> Arguments { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}