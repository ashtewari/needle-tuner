using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace IntentEvalHarness.Tests;

public sealed class IntegrityManifestTests
{
    [Fact]
    public void IntegrityManifest_FileHashesMatchPinnedValues()
    {
        var projectRoot = HarnessPathUtils.GetProjectRoot(AppContext.BaseDirectory);
        var manifestPath = Path.Combine(projectRoot, "integrity.manifest.json");
        Assert.True(File.Exists(manifestPath));

        using var manifestDoc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var files = manifestDoc.RootElement.GetProperty("files").EnumerateArray().ToList();
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var relativePath = file.GetProperty("path").GetString()!;
            var expectedBytes = file.GetProperty("bytes").GetInt32();
            var expectedHash = file.GetProperty("sha256").GetString()!;
            var fullPath = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(fullPath), $"Missing manifest file: {relativePath}");
            var info = new FileInfo(fullPath);
            Assert.Equal(expectedBytes, info.Length);

            using var stream = File.OpenRead(fullPath);
            var actualHash = Convert.ToHexStringLower(SHA256.HashData(stream));
            Assert.Equal(expectedHash, actualHash);
        }
    }
}
