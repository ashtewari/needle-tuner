using Xunit;

namespace IntentEvalHarness.Tests;

public sealed class PathResolutionTests
{
    [Fact]
    public void HarnessPathDiscovery_FindsProjectAndRepositoryRoots()
    {
        var projectRoot = HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory);
        var repositoryRoot = HarnessPathUtils.GetRepositoryRoot(projectRoot);

        Assert.True(File.Exists(Path.Combine(projectRoot, "IntentEvalHarness.csproj")));
        Assert.True(Directory.Exists(Path.Combine(repositoryRoot, "IntentEvalHarness")));
    }

    [Fact]
    public void ResolveInputPath_HandlesHarnessRelativeAndRepoRelativeForms()
    {
        var projectRoot = HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory);
        var repositoryRoot = HarnessPathUtils.GetRepositoryRoot(projectRoot);

        var harnessRelative = Program.ResolveInputPath(projectRoot, repositoryRoot, "Dataset/intent-eval-dataset.json", "summary.json");
        var repoRelative = Program.ResolveInputPath(projectRoot, repositoryRoot, "IntentEvalHarness/Dataset/intent-eval-dataset.json", "summary.json");

        Assert.True(File.Exists(harnessRelative));
        Assert.True(File.Exists(repoRelative));
    }

    [Fact]
    public void ResolveOutputPath_PutsRelativeDatasetOutputUnderHarnessRoot()
    {
        var projectRoot = HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory);
        var repositoryRoot = HarnessPathUtils.GetRepositoryRoot(projectRoot);
        var sourcePath = Program.ResolveInputPath(projectRoot, repositoryRoot, "Dataset/needle-training-seed.json", "seed.json");

        var outputPath = Program.ResolveOutputPath(projectRoot, repositoryRoot, "Dataset/generated.jsonl", sourcePath);
        var expectedPrefix = Path.Combine(projectRoot, "Dataset");

        Assert.StartsWith(expectedPrefix, outputPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveOutputPath_PutsArtifactsOutputUnderRepositoryRoot()
    {
        var projectRoot = HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory);
        var repositoryRoot = HarnessPathUtils.GetRepositoryRoot(projectRoot);
        var sourcePath = Program.ResolveInputPath(projectRoot, repositoryRoot, "Dataset/needle-training-seed.json", "seed.json");

        var outputPath = Program.ResolveOutputPath(projectRoot, repositoryRoot, "artifacts/eval/custom/output.jsonl", sourcePath);
        var expectedPrefix = Path.Combine(repositoryRoot, "artifacts");

        Assert.StartsWith(expectedPrefix, outputPath, StringComparison.OrdinalIgnoreCase);
    }
}
