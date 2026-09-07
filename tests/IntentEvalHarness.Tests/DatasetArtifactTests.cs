using System.Text.Json;
using Xunit;

namespace IntentEvalHarness.Tests;

public sealed class DatasetArtifactTests
{
    private static readonly IReadOnlyDictionary<string, int> HeldOutCounts = new Dictionary<string, int>
    {
        ["SEARCH_ITEM"] = 5, ["ADD_ITEM"] = 5, ["UPDATE_ITEM"] = 5,
        ["DELETE_ITEM"] = 5, ["MANAGE_BOX"] = 5, ["VIEW_INVENTORY"] = 5,
        ["UPLOAD_PHOTO"] = 5, ["GENERAL_HELP"] = 5, ["UNCLEAR"] = 5
    };

    private static readonly IReadOnlyDictionary<string, int> SeedCounts = new Dictionary<string, int>
    {
        ["SEARCH_ITEM"] = 18, ["ADD_ITEM"] = 12, ["UPDATE_ITEM"] = 27,
        ["DELETE_ITEM"] = 12, ["MANAGE_BOX"] = 21, ["VIEW_INVENTORY"] = 20,
        ["UPLOAD_PHOTO"] = 14, ["GENERAL_HELP"] = 13, ["UNCLEAR"] = 30
    };

    private static readonly IReadOnlyDictionary<string, int> ExpandedCounts = new Dictionary<string, int>
    {
        ["SEARCH_ITEM"] = 40, ["ADD_ITEM"] = 30, ["UPDATE_ITEM"] = 53,
        ["DELETE_ITEM"] = 30, ["MANAGE_BOX"] = 44, ["VIEW_INVENTORY"] = 43,
        ["UPLOAD_PHOTO"] = 32, ["GENERAL_HELP"] = 27, ["UNCLEAR"] = 46
    };

    [Fact]
    public void ArtifactManifest_SeparatesAuthoredInputsFromGeneratedOutputs()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(DatasetDirectory, "artifact-manifest.json")));
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        var artifacts = root.GetProperty("artifacts").EnumerateArray().ToList();
        Assert.Equal(8, artifacts.Count);
        Assert.Equal(
            ["intent-eval-dataset.json", "needle-training-seed.json"],
            artifacts.Where(artifact => artifact.GetProperty("provenance").GetString() == "authored")
                .Select(artifact => artifact.GetProperty("path").GetString()!)
                .ToArray());
        Assert.Equal(6, artifacts.Count(artifact => artifact.GetProperty("provenance").GetString() == "generated"));

        foreach (var artifact in artifacts)
        {
            var path = artifact.GetProperty("path").GetString()!;
            Assert.True(File.Exists(Path.Combine(DatasetDirectory, path)), $"Missing dataset artifact: {path}");
        }

        var contract = root.GetProperty("generatorContract");
        Assert.Equal(345, contract.GetProperty("expandedSeed").GetProperty("requiredRows").GetInt32());
        Assert.Equal(391, contract.GetProperty("unclearOversample").GetProperty("requiredRows").GetInt32());
        Assert.Equal("Automation backlog item #11", contract.GetProperty("unclearOversample").GetProperty("owner").GetString());
        Assert.Equal("pwsh -NoProfile -File scripts\\generate-training-datasets.ps1", contract.GetProperty("expandedSeed").GetProperty("command").GetString());
        Assert.Contains("integrity.manifest.json", contract.GetProperty("expandedSeed").GetProperty("postcondition").GetString());
    }

    [Fact]
    public void AuthoredCorpora_AreSchemaValidUniqueAndHeldOut()
    {
        var heldOut = ReadSourceRows("intent-eval-dataset.json");
        var seed = ReadSourceRows("needle-training-seed.json");
        var expanded = ReadSourceRows("needle-training-seed.300.json");

        AssertSourceCorpus(heldOut, 45, HeldOutCounts);
        AssertSourceCorpus(seed, 167, SeedCounts);
        AssertSourceCorpus(expanded, 345, ExpandedCounts);
        Assert.DoesNotContain("\r\n", File.ReadAllText(Path.Combine(DatasetDirectory, "needle-training-seed.300.json")));

        var heldOutInputs = heldOut.Select(row => Normalize(row.GetProperty("input").GetString()!)).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(seed.Select(row => Normalize(row.GetProperty("input").GetString()!)), heldOutInputs.Contains);
        Assert.DoesNotContain(expanded.Select(row => Normalize(row.GetProperty("input").GetString()!)), heldOutInputs.Contains);
    }

    [Fact]
    public void JsonlExports_FollowSourceOrderAndCurrentCounts()
    {
        AssertJsonlMatchesSource("needle-training-seed.json", "training_set.seed.jsonl", 167);
        AssertJsonlMatchesSource("needle-training-seed.300.json", "training_set.300.jsonl", 345);

        AssertSummary("training_set.seed.summary.json", "Dataset/needle-training-seed.json", 167, SeedCounts, 30);
        AssertSummary("training_set.300.summary.json", "Dataset/needle-training-seed.300.json", 345, ExpandedCounts, 46);
    }

    [Fact]
    public void UnclearOversample_IsOrderedAndDuplicatesOnlyUnclearRows()
    {
        var sourceRows = File.ReadAllLines(Path.Combine(DatasetDirectory, "training_set.300.jsonl"));
        var oversampledRows = File.ReadAllLines(Path.Combine(DatasetDirectory, "training_set.300.oversample_unclear.jsonl"));
        var expectedRows = new List<string>(391);

        foreach (var row in sourceRows)
        {
            expectedRows.Add(row);
            using var document = JsonDocument.Parse(row);
            if (document.RootElement.GetProperty("answers").GetArrayLength() == 0)
            {
                expectedRows.Add(row);
            }
        }

        Assert.Equal(345, sourceRows.Length);
        Assert.Equal(391, oversampledRows.Length);
        Assert.Equal(expectedRows, oversampledRows);
        Assert.Equal(46, sourceRows.Count(row => JsonDocument.Parse(row).RootElement.GetProperty("answers").GetArrayLength() == 0));
    }

    [Fact]
    public void FrozenOpenAiBaseline_IsCompletePinnedAndInternallyConsistent()
    {
        var projectRoot = HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory);
        var manifestPath = Path.Combine(projectRoot, "integrity.manifest.json");
        using var integrity = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var frozenPaths = integrity.RootElement.GetProperty("files").EnumerateArray()
            .Where(file => file.GetProperty("path").GetString()!.StartsWith("Baselines/openAi_frozen_20260823_full45/", StringComparison.Ordinal))
            .Select(file => file.GetProperty("path").GetString()!)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "Baselines/openAi_frozen_20260823_full45/README.md",
                "Baselines/openAi_frozen_20260823_full45/manifest.json",
                "Baselines/openAi_frozen_20260823_full45/needle_diagnostics.json",
                "Baselines/openAi_frozen_20260823_full45/results.csv",
                "Baselines/openAi_frozen_20260823_full45/summary.json"
            ],
            frozenPaths);

        var baselineDirectory = Path.Combine(projectRoot, "Baselines", "openAi_frozen_20260823_full45");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(baselineDirectory, "manifest.json")));
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(baselineDirectory, "summary.json")));

        Assert.Equal("openAi_frozen_20260823_full45", manifest.RootElement.GetProperty("baselineId").GetString());
        Assert.Equal("openAi", manifest.RootElement.GetProperty("providerKey").GetString());
        Assert.Equal(45, manifest.RootElement.GetProperty("totalCases").GetInt32());
        Assert.Equal(45, summary.RootElement.GetProperty("totalCases").GetInt32());
        Assert.Equal(45, summary.RootElement.GetProperty("providers").GetProperty("openAi").GetProperty("evaluatedCases").GetInt32());
        Assert.Equal(46, File.ReadAllLines(Path.Combine(baselineDirectory, "results.csv")).Length);
        Assert.Equal(0, JsonDocument.Parse(File.ReadAllText(Path.Combine(baselineDirectory, "needle_diagnostics.json"))).RootElement.GetArrayLength());
    }

    private static string DatasetDirectory => Path.Combine(HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory), "Dataset");

    private static JsonElement[] ReadSourceRows(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(DatasetDirectory, path)));
        return document.RootElement.EnumerateArray().Select(element => element.Clone()).ToArray();
    }

    private static void AssertSourceCorpus(JsonElement[] rows, int expectedCount, IReadOnlyDictionary<string, int> expectedCounts)
    {
        Assert.Equal(expectedCount, rows.Length);
        var inputs = new HashSet<string>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            Assert.Equal(JsonValueKind.String, row.GetProperty("input").ValueKind);
            Assert.Equal(JsonValueKind.String, row.GetProperty("expectedIntent").ValueKind);
            Assert.Equal(JsonValueKind.Object, row.GetProperty("expectedParameters").ValueKind);
            Assert.True(inputs.Add(Normalize(row.GetProperty("input").GetString()!)), "Duplicate normalized input query.");

            var intent = row.GetProperty("expectedIntent").GetString()!;
            counts[intent] = counts.GetValueOrDefault(intent) + 1;
        }

        Assert.Equal(expectedCounts.OrderBy(pair => pair.Key), counts.OrderBy(pair => pair.Key));
    }

    private static void AssertJsonlMatchesSource(string sourceName, string jsonlName, int expectedRows)
    {
        var sourceRows = ReadSourceRows(sourceName);
        var jsonlRows = File.ReadAllLines(Path.Combine(DatasetDirectory, jsonlName));

        Assert.Equal(expectedRows, jsonlRows.Length);
        Assert.Equal(expectedRows, sourceRows.Length);
        var queries = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < sourceRows.Length; index++)
        {
            using var document = JsonDocument.Parse(jsonlRows[index]);
            var row = document.RootElement;
            Assert.Equal(["answers", "query", "reasoning", "tools"], row.EnumerateObject().Select(property => property.Name).OrderBy(name => name));

            var query = row.GetProperty("query").GetString()!;
            Assert.Equal(sourceRows[index].GetProperty("input").GetString(), query);
            Assert.True(queries.Add(Normalize(query)), "Duplicate normalized JSONL query.");
            Assert.Equal(JsonValueKind.Array, row.GetProperty("tools").ValueKind);
            Assert.NotEmpty(row.GetProperty("tools").EnumerateArray());

            var expectedIntent = sourceRows[index].GetProperty("expectedIntent").GetString()!;
            var answers = row.GetProperty("answers");
            if (expectedIntent == "UNCLEAR")
            {
                Assert.Equal(0, answers.GetArrayLength());
            }
            else
            {
                Assert.Equal(1, answers.GetArrayLength());
                Assert.Equal(ToToolName(expectedIntent), answers[0].GetProperty("name").GetString());
                Assert.Equal(JsonValueKind.Object, answers[0].GetProperty("arguments").ValueKind);
            }
        }

        Assert.DoesNotContain("\r\n", File.ReadAllText(Path.Combine(DatasetDirectory, jsonlName)));
    }

    private static void AssertSummary(
        string summaryName,
        string sourcePath,
        int expectedRows,
        IReadOnlyDictionary<string, int> expectedCounts,
        int expectedAbstainRows)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(DatasetDirectory, summaryName)));
        var root = document.RootElement;

        Assert.Equal(sourcePath, root.GetProperty("sourcePath").GetString());
        Assert.Equal(expectedRows, root.GetProperty("totalExamples").GetInt32());
        Assert.Equal(expectedAbstainRows, root.GetProperty("abstainExamples").GetInt32());
        var counts = root.GetProperty("intentCounts").EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetInt32());
        Assert.Equal(expectedCounts.OrderBy(pair => pair.Key), counts.OrderBy(pair => pair.Key));
    }

    private static string Normalize(string input) => string.Join(' ', input.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static string ToToolName(string intent) => intent.ToLowerInvariant() switch
    {
        "search_item" or "add_item" or "update_item" or "delete_item" or "manage_box" or "view_inventory" or "upload_photo" or "general_help" => intent.ToLowerInvariant(),
        _ => throw new InvalidOperationException($"Unexpected training intent: {intent}")
    };
}
