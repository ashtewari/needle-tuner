using System.Text.Json;
using Xunit;

namespace IntentEvalHarness.Tests;

public sealed class JsonlExportTests
{
    [Fact]
    public void ExportJsonl_WritesRowsAndSummary()
    {
        var projectRoot = HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory);
        var repositoryRoot = HarnessPathUtils.GetRepositoryRoot(projectRoot);
        var workDir = Path.Combine(repositoryRoot, "artifacts", "test-temp", $"jsonl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);

        try
        {
            var sourcePath = Path.Combine(workDir, "seed.json");
            var outputPath = Path.Combine(workDir, "training.jsonl");

            var seed = """
            [
              {
                "input": "find my hammer",
                "expectedIntent": "SEARCH_ITEM",
                "expectedParameters": { "itemName": "hammer" },
                "difficulty": "easy"
              },
              {
                "input": "help me label boxes",
                "expectedIntent": "GENERAL_HELP",
                "expectedParameters": { "topic": "labeling" },
                "difficulty": "easy"
              }
            ]
            """;

            File.WriteAllText(sourcePath, seed);
            var export = TrainingDatasetExporter.ExportJsonl(projectRoot, sourcePath, outputPath);

            Assert.True(File.Exists(outputPath));
            Assert.True(File.Exists(Path.Combine(workDir, "training.summary.json")));
            Assert.Equal(2, export.TotalExamples);

            var lines = File.ReadAllLines(outputPath);
            Assert.Equal(2, lines.Length);

            using var row = JsonDocument.Parse(lines[0]);
            var answerTool = row.RootElement.GetProperty("answers")[0].GetProperty("name").GetString();
            Assert.Equal("search_item", answerTool);
        }
        finally
        {
            if (Directory.Exists(workDir))
            {
                Directory.Delete(workDir, recursive: true);
            }
        }
    }
}
