using Xunit;

namespace IntentEvalHarness.Tests;

public sealed class TrainingSkillTests
{
    [Fact]
    public void StandaloneSkill_ReferencesCurrentCommandsAndArtifacts()
    {
        var projectRoot = HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory);
        var repositoryRoot = HarnessPathUtils.GetRepositoryRoot(projectRoot);
        var skillPath = Path.Combine(repositoryRoot, ".github", "skills", "needle-train-eval-loop", "SKILL.md");

        Assert.True(File.Exists(skillPath));
        var skill = File.ReadAllText(skillPath);

        Assert.DoesNotContain("WhichBox", skill, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IntentEvalHarness/bin/", skill, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("weights/EXPERIMENT_LOG.md", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("docs/experiments/EXPERIMENT_LOG.md", skill, StringComparison.Ordinal);
        Assert.Contains("scripts/generate-training-datasets.ps1", skill, StringComparison.Ordinal);
        Assert.Contains("scripts/needle-finetune.sh", skill, StringComparison.Ordinal);
        Assert.Contains("scripts/needle-finetune.ps1", skill, StringComparison.Ordinal);
        Assert.Contains("scripts/bootstrap-wsl.sh --cuda", skill, StringComparison.Ordinal);
        Assert.Contains("scripts/acquire-needle-native.sh", skill, StringComparison.Ordinal);
        Assert.Contains("scripts/prepare-needle-base-checkpoint.sh --download", skill, StringComparison.Ordinal);
        Assert.Contains("artifacts/eval/run_<UTC timestamp>", skill, StringComparison.Ordinal);
        Assert.Contains("[\"weightsFile\"]", skill, StringComparison.Ordinal);
        Assert.Contains("$($manifest.weightsFile)", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("whichbox_needle_tuned.cact", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--epochs 15 --lora-rank 16 --lora-alpha 32 --learning-rate 0.0001", skill, StringComparison.Ordinal);
        Assert.Contains("--batch-size 4 --max-len 1024 --val-split 0.1", skill, StringComparison.Ordinal);

        foreach (var relativePath in new[]
        {
            "scripts/generate-training-datasets.ps1",
            "scripts/needle-finetune.sh",
            "scripts/needle-finetune.ps1",
            "scripts/bootstrap-wsl.sh",
            "scripts/acquire-needle-native.sh",
            "scripts/bootstrap-needle.ps1",
            "scripts/acquire-needle-native.ps1",
            "scripts/prepare-needle-base-checkpoint.sh",
            "docs/experiments/EXPERIMENT_LOG.md",
            "IntentEvalHarness/Dataset/intent-eval-dataset.json",
            "IntentEvalHarness/Baselines/openAi_frozen_20260823_full45/summary.json"
        })
        {
            Assert.True(File.Exists(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))), $"Missing skill reference: {relativePath}");
        }
    }
}
