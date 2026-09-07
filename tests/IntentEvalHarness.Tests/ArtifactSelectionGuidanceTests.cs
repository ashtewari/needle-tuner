using Xunit;

namespace IntentEvalHarness.Tests;

public sealed class ArtifactSelectionGuidanceTests
{
    [Fact]
    public void CurrentGuidance_UsesTheStandaloneArtifactAndManifestSelection()
    {
        var projectRoot = HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory);
        var repositoryRoot = HarnessPathUtils.GetRepositoryRoot(projectRoot);
        var guidance = new[]
        {
            "README.md",
            "IntentEvalHarness/weights/README.md",
            ".github/skills/needle2-train-eval-loop/SKILL.md"
        };

        foreach (var relativePath in guidance)
        {
            var content = File.ReadAllText(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.DoesNotContain("whichbox_needle_tuned.cact", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("weightsFile", content, StringComparison.Ordinal);
            Assert.Contains("TunedWeightsSha256", content, StringComparison.Ordinal);
            Assert.Contains("TunedProviderKey", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FineTuneWrappers_EmitTheCanonicalManifestNamedArtifact()
    {
        var projectRoot = HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory);
        var repositoryRoot = HarnessPathUtils.GetRepositoryRoot(projectRoot);
        var bashWrapper = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "needle-finetune.sh"));
        var powerShellWrapper = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "needle-finetune.ps1"));

        Assert.Contains("artifact_file_name=\"needle_tuned.cact\"", bashWrapper, StringComparison.Ordinal);
        Assert.Contains("weightsFile", bashWrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("whichbox_needle_tuned.cact", bashWrapper, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$artifactFileName = 'needle_tuned.cact'", powerShellWrapper, StringComparison.Ordinal);
        Assert.Contains("weightsFile", powerShellWrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("whichbox_needle_tuned.cact", powerShellWrapper, StringComparison.OrdinalIgnoreCase);
    }
}
