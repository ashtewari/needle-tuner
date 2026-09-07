using Xunit;

namespace IntentEvalHarness.Tests;

public sealed class BaseCheckpointPreparationTests
{
    [Fact]
    public void PreparationScript_UsesExplicitVerifiedOfficialOrImportFlow()
    {
        var projectRoot = HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory);
        var repositoryRoot = HarnessPathUtils.GetRepositoryRoot(projectRoot);
        var scriptPath = Path.Combine(repositoryRoot, "scripts", "prepare-needle-base-checkpoint.sh");

        Assert.True(File.Exists(scriptPath));
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("Cactus-Compute/needle2", script, StringComparison.Ordinal);
        Assert.Contains("checkpoints/needle2.pkl", script, StringComparison.Ordinal);
        Assert.Contains("--download", script, StringComparison.Ordinal);
        Assert.Contains("--source", script, StringComparison.Ordinal);
        Assert.Contains("--sha256", script, StringComparison.Ordinal);
        Assert.Contains("hf_hub_download", script, StringComparison.Ordinal);
        Assert.Contains("get_hf_file_metadata", script, StringComparison.Ordinal);
        Assert.Contains("base-checkpoint.manifest.json", script, StringComparison.Ordinal);
        Assert.Contains("for attempt in 1 2 3", script, StringComparison.Ordinal);
        Assert.DoesNotContain("whichboxapp", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrainingEntryPoints_RequireRecordedCheckpointProvenance()
    {
        var projectRoot = HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory);
        var repositoryRoot = HarnessPathUtils.GetRepositoryRoot(projectRoot);
        var bootstrap = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "bootstrap-wsl.sh"));
        var bashWrapper = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "needle-finetune.sh"));
        var powerShellWrapper = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "needle-finetune.ps1"));

        Assert.Contains("--prepare-base-checkpoint", bootstrap, StringComparison.Ordinal);
        Assert.Contains("prepare-needle-base-checkpoint.sh", bootstrap, StringComparison.Ordinal);
        Assert.Contains("base-checkpoint.manifest.json", bashWrapper, StringComparison.Ordinal);
        Assert.Contains("base-checkpoint.manifest.json", powerShellWrapper, StringComparison.Ordinal);
    }
}
