using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace IntentEvalHarness.Tests;

public sealed class ReleaseEvidenceBundleTests
{
    [Fact]
    public void ValidationBundle_IsSelfContainedSanitizedAndChecksumCovered()
    {
        var projectRoot = HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory);
        var repositoryRoot = HarnessPathUtils.GetRepositoryRoot(projectRoot);
        var bundleRelativePath = "docs/experiments/provenance/20260905_validation_full";
        var bundlePath = Path.Combine(repositoryRoot, bundleRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var manifestPath = Path.Combine(bundlePath, "manifest.json");
        var manifestText = File.ReadAllText(manifestPath);
        using var manifest = JsonDocument.Parse(manifestText);

        Assert.DoesNotContain(".squad/", manifestText, StringComparison.OrdinalIgnoreCase);
        var references = manifest.RootElement.GetProperty("evidenceBasis").GetProperty("publishedTargetReferences");
        foreach (var reference in references.EnumerateArray())
        {
            var fullPath = Path.Combine(repositoryRoot, reference.GetString()!.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(fullPath) || Directory.Exists(fullPath), $"Missing published evidence reference: {reference}");
        }

        var verification = manifest.RootElement.GetProperty("verification");
        Assert.Equal("32/32", verification.GetProperty("historicalCleanRoomPostRun").GetProperty("dotnetTests").GetString());
        Assert.Equal("36/36", verification.GetProperty("laterReviewerFix").GetProperty("dotnetTests").GetString());

        var retainedFiles = manifest.RootElement.GetProperty("retainedFiles")
            .EnumerateArray()
            .Select(element => element.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        var checksumLines = File.ReadAllLines(Path.Combine(bundlePath, "checksums.sha256"));
        Assert.Equal(retainedFiles.Count, checksumLines.Length);
        foreach (var line in checksumLines)
        {
            var fields = line.Split("  ", StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, fields.Length);
            Assert.Contains(fields[1], retainedFiles);
            var fullPath = Path.Combine(bundlePath, fields[1]);
            Assert.Equal(fields[0], Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(fullPath))));
        }

        foreach (var file in retainedFiles)
        {
            var content = File.ReadAllText(Path.Combine(bundlePath, file));
            Assert.DoesNotContain(".squad/", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(new Regex(@"(?i)(?:[a-z]:\\|/(?:home|mnt)/)"), content);
        }
    }
}
