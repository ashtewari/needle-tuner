using Microsoft.Extensions.Configuration;
using Xunit;

namespace IntentEvalHarness.Tests;

public sealed class WeightRegistryTests
{
    [Fact]
    public void GetPreferredTunedArtifact_DiscoversManifestAndValidatesHash()
    {
        var workDir = CreateWorkDir("weights-discovery");
        var weightsDir = Path.Combine(workDir, "weights", "runA");
        Directory.CreateDirectory(weightsDir);

        var weightsPath = Path.Combine(weightsDir, "needle_tuned.cact");
        File.WriteAllBytes(weightsPath, [1, 2, 3, 4, 5]);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(weightsPath)));

        File.WriteAllText(
            Path.Combine(weightsDir, "manifest.json"),
            $$"""
            {
              "providerKey": "needleRunA",
              "displayName": "Needle Tuned Run A",
              "weightsFile": "needle_tuned.cact",
              "sha256": "{{hash}}"
            }
            """);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var registry = new NeedleWeightRegistry(workDir, config);
        var artifact = registry.GetPreferredTunedArtifact();

        Assert.NotNull(artifact);
        Assert.Equal("needleRunA", artifact!.ProviderKey);
        registry.Validate(artifact);

        Directory.Delete(workDir, recursive: true);
    }

    [Fact]
    public void Validate_ThrowsWhenConfiguredHashDoesNotMatch()
    {
        var workDir = CreateWorkDir("weights-hash-mismatch");
        var weightsDir = Path.Combine(workDir, "weights");
        Directory.CreateDirectory(weightsDir);

        var weightsPath = Path.Combine(weightsDir, "needle_tuned.cact");
        File.WriteAllBytes(weightsPath, [9, 9, 9]);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Needle:TunedWeightsPath"] = "weights/needle_tuned.cact",
                ["Needle:TunedWeightsSha256"] = "deadbeef"
            })
            .Build();

        var registry = new NeedleWeightRegistry(workDir, config);
        var artifact = registry.GetPreferredTunedArtifact();

        Assert.NotNull(artifact);
        Assert.Throws<InvalidOperationException>(() => registry.Validate(artifact!));

        Directory.Delete(workDir, recursive: true);
    }

    [Fact]
    public void GetPreferredTunedArtifact_IgnoresManifestPathTraversal()
    {
        var workDir = CreateWorkDir("weights-traversal");
        var weightsDir = Path.Combine(workDir, "weights", "runA");
        Directory.CreateDirectory(weightsDir);
        File.WriteAllBytes(Path.Combine(workDir, "outside.cact"), [1, 2, 3]);

        File.WriteAllText(
            Path.Combine(weightsDir, "manifest.json"),
            """
            {
              "providerKey": "needleRunA",
              "displayName": "Needle Tuned Run A",
              "weightsFile": "../../outside.cact",
              "sha256": "039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81"
            }
            """);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var registry = new NeedleWeightRegistry(workDir, config);

        Assert.Null(registry.GetPreferredTunedArtifact());
        Directory.Delete(workDir, recursive: true);
    }

    [Fact]
    public void GetPreferredTunedArtifact_IgnoresManifestWithoutSha256()
    {
        var workDir = CreateWorkDir("weights-missing-hash");
        var weightsDir = Path.Combine(workDir, "weights", "runA");
        Directory.CreateDirectory(weightsDir);
        File.WriteAllBytes(Path.Combine(weightsDir, "needle_tuned.cact"), [1, 2, 3]);

        File.WriteAllText(
            Path.Combine(weightsDir, "manifest.json"),
            """
            {
              "providerKey": "needleRunA",
              "displayName": "Needle Tuned Run A",
              "weightsFile": "needle_tuned.cact"
            }
            """);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var registry = new NeedleWeightRegistry(workDir, config);

        Assert.Null(registry.GetPreferredTunedArtifact());
        Directory.Delete(workDir, recursive: true);
    }

    [Fact]
    public void GetPreferredTunedArtifact_IsEngineVersionAware()
    {
        var workDir = CreateWorkDir("weights-engine-version");
        var v2Dir = Path.Combine(workDir, "weights", "runV2");
        var v3Dir = Path.Combine(workDir, "weights", "runV3");
        Directory.CreateDirectory(v2Dir);
        Directory.CreateDirectory(v3Dir);

        var v2WeightsPath = Path.Combine(v2Dir, "needle_tuned.cact");
        File.WriteAllBytes(v2WeightsPath, [1, 2, 3]);
        var v2Hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(v2WeightsPath)));
        File.WriteAllText(
            Path.Combine(v2Dir, "manifest.json"),
            $$"""
            {
              "providerKey": "needleRunV2",
              "displayName": "Needle Tuned Run V2",
              "weightsFile": "needle_tuned.cact",
              "sha256": "{{v2Hash}}"
            }
            """);

        var v3WeightsPath = Path.Combine(v3Dir, "needle_tuned.cact");
        File.WriteAllBytes(v3WeightsPath, [4, 5, 6]);
        var v3Hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(v3WeightsPath)));
        File.WriteAllText(
            Path.Combine(v3Dir, "manifest.json"),
            $$"""
            {
              "providerKey": "needleRunV3",
              "displayName": "Needle Tuned Run V3",
              "weightsFile": "needle_tuned.cact",
              "sha256": "{{v3Hash}}",
              "engineVersion": "3.0.2"
            }
            """);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var registry = new NeedleWeightRegistry(workDir, config);

        var v2Artifact = registry.GetPreferredTunedArtifact("2.0.10");
        var v3Artifact = registry.GetPreferredTunedArtifact("3.0.2");

        Assert.NotNull(v2Artifact);
        Assert.Equal("needleRunV2", v2Artifact!.ProviderKey);
        Assert.NotNull(v3Artifact);
        Assert.Equal("needleRunV3", v3Artifact!.ProviderKey);

        Directory.Delete(workDir, recursive: true);
    }

    private static string CreateWorkDir(string prefix)
    {
        var projectRoot = HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory);
        var repositoryRoot = HarnessPathUtils.GetRepositoryRoot(projectRoot);
        var workDir = Path.Combine(repositoryRoot, "artifacts", "test-temp", $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);

        File.WriteAllText(Path.Combine(workDir, "IntentEvalHarness.csproj"), "<Project />");
        return workDir;
    }
}
