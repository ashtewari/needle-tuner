using System.Reflection;
using Xunit;

namespace IntentEvalHarness.Tests;

public sealed class NativeLibraryResolutionTests
{
    [Fact]
    public void NativeLibraryCandidates_IncludeCurrentRidAcquisitionLocation()
    {
        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64
            ? "arm64"
            : "x64";
        var platform = OperatingSystem.IsWindows() ? "win" : "linux";
        var library = OperatingSystem.IsWindows() ? "libneedle.dll" : "libneedle.so";
        var expectedSuffix = Path.Combine("native", $"{platform}-{architecture}", library);

        var method = typeof(NeedleIntentClient).GetMethod("GetNativeLibraryCandidates", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var candidates = ((IEnumerable<string>)method!.Invoke(null, null)!).ToArray();

        Assert.Contains(candidates, candidate => candidate.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase));
    }
}
