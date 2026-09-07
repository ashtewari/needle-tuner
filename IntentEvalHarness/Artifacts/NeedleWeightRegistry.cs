using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace IntentEvalHarness;

public sealed class NeedleWeightArtifact
{
    public string ProviderKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string WeightsPath { get; init; } = string.Empty;
    public string? ManifestPath { get; init; }
    public string? Sha256 { get; init; }
    public bool FileExists { get; init; }
    public long? FileSizeBytes { get; init; }
}

public sealed class NeedleWeightRegistry
{
    private readonly string _projectRoot;
    private readonly string _repositoryRoot;
    private readonly IConfiguration _configuration;
    private readonly JsonSerializerOptions _serializerOptions = new() { PropertyNameCaseInsensitive = true };

    public NeedleWeightRegistry(string projectRoot, IConfiguration configuration)
    {
        _projectRoot = projectRoot;
        _repositoryRoot = HarnessPathUtils.GetRepositoryRoot(projectRoot, configuration["Harness:RepositoryRoot"]);
        _configuration = configuration;
    }

    public NeedleWeightArtifact? GetPreferredTunedArtifact()
    {
        var configuredArtifact = TryGetConfiguredArtifact();
        if (configuredArtifact is not null)
        {
            return configuredArtifact;
        }

        return DiscoverManifestArtifacts().LastOrDefault();
    }

    public string Describe(NeedleWeightArtifact artifact)
    {
        var sizeText = artifact.FileSizeBytes.HasValue
            ? $", {artifact.FileSizeBytes.Value} bytes"
            : string.Empty;
        return $"{artifact.DisplayName} [{artifact.WeightsPath}]{sizeText}";
    }

    public void Validate(NeedleWeightArtifact artifact)
    {
        if (!File.Exists(artifact.WeightsPath))
        {
            throw new FileNotFoundException($"Configured Needle weights file was not found: {artifact.WeightsPath}", artifact.WeightsPath);
        }

        if (string.IsNullOrWhiteSpace(artifact.Sha256))
        {
            return;
        }

        using var stream = File.OpenRead(artifact.WeightsPath);
        var actualHash = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (!string.Equals(actualHash, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Needle weights SHA-256 mismatch for '{artifact.DisplayName}'. Expected {artifact.Sha256}, got {actualHash}.");
        }
    }

    private NeedleWeightArtifact? TryGetConfiguredArtifact()
    {
        var configuredPath = _configuration["Needle:TunedWeightsPath"];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        var resolvedPath = ResolveConfiguredPath(configuredPath);
        var displayName = _configuration["Needle:TunedWeightsDisplayName"];
        var key = _configuration["Needle:TunedProviderKey"];
        var fileExists = File.Exists(resolvedPath);

        return new NeedleWeightArtifact
        {
            ProviderKey = SanitizeProviderKey(key, Path.GetFileNameWithoutExtension(resolvedPath)),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? $"Needle Tuned ({Path.GetFileNameWithoutExtension(resolvedPath)})" : displayName,
            WeightsPath = resolvedPath,
            Sha256 = _configuration["Needle:TunedWeightsSha256"],
            FileExists = fileExists,
            FileSizeBytes = fileExists ? new FileInfo(resolvedPath).Length : null
        };
    }

    private IReadOnlyList<NeedleWeightArtifact> DiscoverManifestArtifacts()
    {
        var manifests = GetCandidateWeightsRoots()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
            .Where(path => string.Equals(Path.GetFileName(path), "manifest.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var artifacts = new List<NeedleWeightArtifact>();
        foreach (var manifestPath in manifests)
        {
            var manifest = JsonSerializer.Deserialize<NeedleWeightManifest>(File.ReadAllText(manifestPath), _serializerOptions);
            if (manifest is null)
            {
                continue;
            }

            var weightsFile = manifest.WeightsFile ?? manifest.WeightsPath;
            if (string.IsNullOrWhiteSpace(weightsFile))
            {
                continue;
            }

            var resolvedWeightsPath = TryResolveManifestWeightsPath(manifestPath, weightsFile);
            if (resolvedWeightsPath is null || !IsSha256(manifest.Sha256))
            {
                continue;
            }

            var fileExists = File.Exists(resolvedWeightsPath);
            if (!fileExists)
            {
                continue;
            }

            var directoryName = Path.GetFileName(Path.GetDirectoryName(manifestPath)) ?? "Tuned";

            artifacts.Add(new NeedleWeightArtifact
            {
                ProviderKey = SanitizeProviderKey(manifest.ProviderKey ?? manifest.Key, directoryName),
                DisplayName = string.IsNullOrWhiteSpace(manifest.DisplayName) ? $"Needle Tuned ({directoryName})" : manifest.DisplayName,
                WeightsPath = resolvedWeightsPath,
                ManifestPath = manifestPath,
                Sha256 = manifest.Sha256,
                FileExists = fileExists,
                FileSizeBytes = fileExists ? new FileInfo(resolvedWeightsPath).Length : null
            });
        }

        return artifacts;
    }

    private IEnumerable<string> GetCandidateWeightsRoots()
    {
        yield return Path.Combine(_projectRoot, "weights");
        yield return Path.Combine(_repositoryRoot, "IntentEvalHarness", "weights");
        yield return Path.Combine(_repositoryRoot, "weights");
    }

    private string ResolveConfiguredPath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(_projectRoot, configuredPath)),
            Path.GetFullPath(Path.Combine(_repositoryRoot, configuredPath)),
            Path.GetFullPath(Path.Combine(_repositoryRoot, "IntentEvalHarness", configuredPath))
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static string? TryResolveManifestWeightsPath(string manifestPath, string weightsFile)
    {
        if (Path.IsPathRooted(weightsFile))
        {
            return null;
        }

        var manifestDirectory = Path.GetFullPath(Path.GetDirectoryName(manifestPath) ?? string.Empty);
        var resolvedPath = Path.GetFullPath(Path.Combine(manifestDirectory, weightsFile));
        var relativePath = Path.GetRelativePath(manifestDirectory, resolvedPath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath) ||
            !string.Equals(Path.GetExtension(resolvedPath), ".cact", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return resolvedPath;
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string SanitizeProviderKey(string? configuredKey, string fallbackName)
    {
        var source = string.IsNullOrWhiteSpace(configuredKey) ? fallbackName : configuredKey;
        var chars = source.Where(char.IsLetterOrDigit).ToArray();
        var sanitized = chars.Length == 0 ? "needleTuned" : new string(chars);
        return sanitized.StartsWith("needle", StringComparison.OrdinalIgnoreCase)
            ? sanitized
            : $"needle{sanitized}";
    }

    private sealed class NeedleWeightManifest
    {
        public string? Key { get; set; }
        public string? ProviderKey { get; set; }
        public string? DisplayName { get; set; }
        public string? WeightsFile { get; set; }
        public string? WeightsPath { get; set; }
        public string? Sha256 { get; set; }
    }
}
