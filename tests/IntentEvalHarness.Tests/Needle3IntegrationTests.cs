using Xunit;

namespace IntentEvalHarness.Tests;

public sealed class Needle3IntegrationTests
{
    private static EvalProvider FakeProvider(string key, bool enabled = true, string? engineFamily = null) =>
        new(
            key: key,
            displayName: key,
            enabled: enabled,
            evaluateAsync: _ => Task.FromResult(new ProviderOutcome()),
            engineFamily: engineFamily);

    [Fact]
    public void SelectProviders_RejectsNeedleV3WithNeedleBase()
    {
        var allProviders = new List<EvalProvider>
        {
            FakeProvider("needleBase", engineFamily: "needle2"),
            FakeProvider("needleV3", engineFamily: "needle3")
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            Program.SelectProviders(allProviders, ["needleBase", "needleV3"]));

        Assert.Contains("multiple Needle engine families", ex.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be loaded in the same process", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectProviders_RejectsNeedleV3WithTunedNeedle2Key()
    {
        var allProviders = new List<EvalProvider>
        {
            FakeProvider("needleMyTunedRun", engineFamily: "needle2"),
            FakeProvider("needleV3", engineFamily: "needle3")
        };

        Assert.Throws<ArgumentException>(() =>
            Program.SelectProviders(allProviders, ["needleMyTunedRun", "needleV3"]));
    }

    [Fact]
    public void SelectProviders_AllowsNeedleV3Alone()
    {
        var allProviders = new List<EvalProvider>
        {
            FakeProvider("needleBase", engineFamily: "needle2"),
            FakeProvider("needleV3", engineFamily: "needle3")
        };

        var selected = Program.SelectProviders(allProviders, ["needleV3"]);

        Assert.Single(selected);
        Assert.Equal("needleV3", selected[0].Key);
    }

    [Fact]
    public void SelectProviders_AllowsNeedleV3WithOpenAi()
    {
        var allProviders = new List<EvalProvider>
        {
            FakeProvider("openAi"),
            FakeProvider("needleV3", engineFamily: "needle3")
        };

        var selected = Program.SelectProviders(allProviders, ["openAi", "needleV3"]);

        Assert.Equal(2, selected.Count);
    }

    [Fact]
    public void SelectProviders_AllowsNeedleV3WithTunedNeedle3Key()
    {
        var allProviders = new List<EvalProvider>
        {
            FakeProvider("needleV3", engineFamily: "needle3"),
            FakeProvider("needleMyV3TunedRun", engineFamily: "needle3")
        };

        var selected = Program.SelectProviders(allProviders, ["needleV3", "needleMyV3TunedRun"]);

        Assert.Equal(2, selected.Count);
    }

    [Fact]
    public void AppSettings_DefinesNeedleV3ConfigurationKeys()
    {
        var projectRoot = HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory);
        var appsettings = File.ReadAllText(Path.Combine(projectRoot, "appsettings.json"));

        Assert.Contains("V3NativeLibraryPath", appsettings, StringComparison.Ordinal);
        Assert.Contains("V3WeightsPath", appsettings, StringComparison.Ordinal);
        Assert.Contains("V3WeightsDisplayName", appsettings, StringComparison.Ordinal);
        Assert.Contains("V3WeightsSha256", appsettings, StringComparison.Ordinal);
        Assert.Contains("weights/base-v3/needle3.cact", appsettings, StringComparison.Ordinal);
        Assert.Contains("V3TunedWeightsPath", appsettings, StringComparison.Ordinal);
        Assert.Contains("V3TunedProviderKey", appsettings, StringComparison.Ordinal);
        Assert.Contains("V3TunedWeightsDisplayName", appsettings, StringComparison.Ordinal);
        Assert.Contains("V3TunedWeightsSha256", appsettings, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("scripts/bootstrap-wsl.sh")]
    [InlineData("scripts/bootstrap-needle.ps1")]
    [InlineData("scripts/acquire-needle-native.sh")]
    [InlineData("scripts/acquire-needle-native.ps1")]
    public void EngineVersionScripts_PinBothNeedle2AndNeedle3Wheels(string relativePath)
    {
        var projectRoot = HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory);
        var repositoryRoot = HarnessPathUtils.GetRepositoryRoot(projectRoot);
        var script = File.ReadAllText(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains("2.0.10", script, StringComparison.Ordinal);
        Assert.Contains("3.0.2", script, StringComparison.Ordinal);
        Assert.Contains("f263b8e74fde5e225bb19a1c2747a3c2164272e27f1a7b948ecea3f3e1d925d9", script, StringComparison.Ordinal);
        Assert.Contains("99200776c42b2af93325326f1030b49da6af3fa9d66e5d979b44f5e472e4e739", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RequirementsCactusNeedleV3_PinsExpectedWheelHash()
    {
        var projectRoot = HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory);
        var repositoryRoot = HarnessPathUtils.GetRepositoryRoot(projectRoot);
        var requirements = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "requirements-cactus-needle-v3.txt"));

        Assert.Contains("cactus-needle==3.0.2", requirements, StringComparison.Ordinal);
        Assert.Contains("99200776c42b2af93325326f1030b49da6af3fa9d66e5d979b44f5e472e4e739", requirements, StringComparison.Ordinal);
    }

    [Fact]
    public void RequirementsNeedleV3_PinsFullTrainingStack()
    {
        var projectRoot = HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory);
        var repositoryRoot = HarnessPathUtils.GetRepositoryRoot(projectRoot);
        var requirements = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "requirements-needle-v3.txt"));

        Assert.Contains("jax==0.11.1", requirements, StringComparison.Ordinal);
        Assert.Contains("jaxlib==0.11.1", requirements, StringComparison.Ordinal);
        Assert.Contains("flax==0.12.9", requirements, StringComparison.Ordinal);
        Assert.Contains("optax==0.2.8", requirements, StringComparison.Ordinal);
        Assert.Contains("safetensors==0.8.0", requirements, StringComparison.Ordinal);
        Assert.DoesNotContain("inference-only", requirements, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("scripts/bootstrap-wsl.sh")]
    [InlineData("scripts/bootstrap-needle.ps1")]
    public void BootstrapScripts_NoLongerRestrictCudaOrTrainingStackByEngineVersion(string relativePath)
    {
        var repositoryRoot = HarnessPathUtils.GetRepositoryRoot(HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory));
        var script = File.ReadAllText(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.DoesNotContain("inference-only", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isInferenceOnly", script, StringComparison.Ordinal);
        Assert.DoesNotContain("is_inference_only", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("scripts/needle-finetune.sh")]
    [InlineData("scripts/needle-finetune.ps1")]
    public void FinetuneWrappers_SupportEngineVersionAndPreserveNeedle2Defaults(string relativePath)
    {
        var repositoryRoot = HarnessPathUtils.GetRepositoryRoot(HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory));
        var script = File.ReadAllText(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains("3.0.2", script, StringComparison.Ordinal);
        Assert.Contains("needle3.safetensors", script, StringComparison.Ordinal);
        Assert.Contains("needle_lora.safetensors", script, StringComparison.Ordinal);
        Assert.Contains("engineVersion", script, StringComparison.Ordinal);
    }

    [Fact]
    public void PrepareBaseCheckpointScripts_SupportRepositoryAndFilenameOverrides()
    {
        var projectRoot = HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory);
        var repositoryRoot = HarnessPathUtils.GetRepositoryRoot(projectRoot);
        var bashScriptPath = Path.Combine(repositoryRoot, "scripts", "prepare-needle-base-checkpoint.sh");
        var powerShellScriptPath = Path.Combine(repositoryRoot, "scripts", "prepare-needle-base-checkpoint.ps1");

        Assert.True(File.Exists(powerShellScriptPath));

        var bashScript = File.ReadAllText(bashScriptPath);
        var powerShellScript = File.ReadAllText(powerShellScriptPath);

        Assert.Contains("--repository", bashScript, StringComparison.Ordinal);
        Assert.Contains("--filename", bashScript, StringComparison.Ordinal);
        Assert.Contains("Cactus-Compute/needle2", bashScript, StringComparison.Ordinal);
        Assert.Contains("checkpoints/needle2.pkl", bashScript, StringComparison.Ordinal);

        Assert.Contains("[string]$Repository", powerShellScript, StringComparison.Ordinal);
        Assert.Contains("[string]$Filename", powerShellScript, StringComparison.Ordinal);
        Assert.Contains("Cactus-Compute/needle2", powerShellScript, StringComparison.Ordinal);
        Assert.Contains("checkpoints/needle2.pkl", powerShellScript, StringComparison.Ordinal);
        Assert.Contains("base-checkpoint.manifest.json", powerShellScript, StringComparison.Ordinal);
    }
}

