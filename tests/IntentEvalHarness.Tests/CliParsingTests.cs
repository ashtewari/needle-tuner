using Xunit;

namespace IntentEvalHarness.Tests;

public sealed class CliParsingTests
{
    [Fact]
    public void ParseCommandLineOptions_ParsesExportMode()
    {
        var options = Program.ParseCommandLineOptions(
            ["--export-training-jsonl", "Dataset/needle-training-seed.json", "--output", "Dataset/training_set.seed.jsonl"],
            totalCases: 45);

        Assert.Equal("Dataset/needle-training-seed.json", options.TrainingSourcePath);
        Assert.Equal("Dataset/training_set.seed.jsonl", options.TrainingOutputPath);
        Assert.Null(options.SelectedOrdinals);
    }

    [Fact]
    public void ParseCommandLineOptions_RejectsOutputWithoutExport()
    {
        var ex = Assert.Throws<ArgumentException>(() => Program.ParseCommandLineOptions(["--output", "out.jsonl"], 45));
        Assert.Contains("--output can only be used with --export-training-jsonl.", ex.Message);
    }

    [Fact]
    public void ParseCommandLineOptions_RequiresBaselineForCompareMetrics()
    {
        var ex = Assert.Throws<ArgumentException>(() => Program.ParseCommandLineOptions(["--compare-metrics", "runtime"], 45));
        Assert.Contains("--compare-metrics requires --compare-baseline.", ex.Message);
    }
}
