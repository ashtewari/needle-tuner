namespace IntentEvalHarness;

public static class HarnessPathUtils
{
    public static string GetProjectRoot(string baseDir)
    {
        var start = Path.GetFullPath(baseDir);
        foreach (var candidate in EnumerateSelfAndAncestors(start))
        {
            if (IsHarnessRoot(candidate))
            {
                return candidate;
            }

            var nestedHarness = Path.Combine(candidate, "IntentEvalHarness");
            if (IsHarnessRoot(nestedHarness))
            {
                return nestedHarness;
            }
        }

        throw new InvalidOperationException(
            $"Could not locate IntentEvalHarness project root from '{baseDir}'. " +
            "Expected to find IntentEvalHarness.csproj in this directory tree.");
    }

    public static string GetRepositoryRoot(string projectRoot, string? configuredRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            return Path.IsPathRooted(configuredRoot)
                ? Path.GetFullPath(configuredRoot)
                : Path.GetFullPath(Path.Combine(projectRoot, configuredRoot));
        }

        var harnessParent = Directory.GetParent(projectRoot)?.FullName;
        if (!string.IsNullOrWhiteSpace(harnessParent))
        {
            return harnessParent;
        }

        return projectRoot;
    }

    public static string ToProjectRelative(string projectRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var absolutePath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(projectRoot, path));

        var relativePath = Path.GetRelativePath(projectRoot, absolutePath);
        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static bool IsHarnessRoot(string path)
    {
        return Directory.Exists(path) && File.Exists(Path.Combine(path, "IntentEvalHarness.csproj"));
    }

    private static IEnumerable<string> EnumerateSelfAndAncestors(string start)
    {
        var current = start;
        while (!string.IsNullOrWhiteSpace(current))
        {
            yield return current;

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                yield break;
            }

            current = parent.FullName;
        }
    }
}