using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IntentEvalHarness;

public class IntentEvalCase
{
    public string Input { get; set; } = string.Empty;
    public string ExpectedIntent { get; set; } = string.Empty;
    public Dictionary<string, object> ExpectedParameters { get; set; } = new();
    public string? Notes { get; set; }
}

public class ProviderOutcome
{
    public string? Intent { get; set; }
    public double? Confidence { get; set; }
    public bool IntentMatch { get; set; }
    public bool ParamMatch { get; set; }
    public string? Error { get; set; }
    public string? ResponseType { get; set; }
    public string? ToolName { get; set; }
    public string? DiagnosticKind { get; set; }
    public string? FallbackReason { get; set; }
    public string? RawArgumentsJson { get; set; }
    public string? RawResponseJson { get; set; }
    public int? DurationMs { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public bool? TokensEstimated { get; set; }
    public decimal? CostUsd { get; set; }
    public string? CostSource { get; set; }
    public string? ModelName { get; set; }
}

[Flags]
public enum BaselineComparisonMetrics
{
    None = 0,
    Runtime = 1,
    Cost = 2
}

public sealed class EvalProvider
{
    public EvalProvider(
        string key,
        string displayName,
        bool enabled,
        Func<IntentEvalCase, Task<ProviderOutcome>> evaluateAsync,
        Func<Task>? initializeAsync = null,
        Action? disposeAction = null)
    {
        Key = key;
        DisplayName = displayName;
        Enabled = enabled;
        EvaluateAsync = evaluateAsync;
        InitializeAsync = initializeAsync;
        DisposeAction = disposeAction;
    }

    public string Key { get; }
    public string DisplayName { get; }
    public bool Enabled { get; }
    public Func<IntentEvalCase, Task<ProviderOutcome>> EvaluateAsync { get; }
    public Func<Task>? InitializeAsync { get; }
    public Action? DisposeAction { get; }
}

public sealed class EvalRow
{
    public int Ordinal { get; init; }
    public IntentEvalCase Case { get; init; } = new();
    public Dictionary<string, ProviderOutcome> Outcomes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ConfiguredProviderInfo
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool Enabled { get; init; }
}

public sealed class ConfidenceThresholdSummary
{
    public double Threshold { get; init; }
    public int? CasesAtOrAboveThreshold { get; init; }
    public int? CasesBelowThreshold { get; init; }
    public double? Coverage { get; init; }
    public double? IntentAccuracyAtOrAboveThreshold { get; init; }
    public double? ParameterAccuracyAtOrAboveThreshold { get; init; }
}

public sealed class IntentBreakdownSummary
{
    public int TotalCases { get; init; }
    public int EvaluatedCases { get; init; }
    public int ErrorCases { get; init; }
    public double IntentAccuracy { get; init; }
    public double ParameterAccuracy { get; init; }
    public Dictionary<string, int> PredictedIntentCounts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProviderSummaryReport
{
    public string DisplayName { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public int EvaluatedCases { get; init; }
    public int ErrorCases { get; init; }
    public double IntentAccuracy { get; init; }
    public double ParameterAccuracy { get; init; }
    public double? AverageConfidence { get; init; }
    public long? TotalDurationMs { get; init; }
    public double? AverageDurationMs { get; init; }
    public int RuntimeMeasuredCases { get; init; }
    public int? TotalInputTokens { get; init; }
    public int? TotalOutputTokens { get; init; }
    public int TokenUsageCases { get; init; }
    public int TokenEstimatedCases { get; init; }
    public decimal? TotalCostUsd { get; init; }
    public decimal? AverageCostUsd { get; init; }
    public int CostMeasuredCases { get; init; }
    public string? CostSource { get; init; }
    public string? ModelName { get; init; }
    public Dictionary<string, IntentBreakdownSummary> PerIntent { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, int>> ConfusionMatrix { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ConfidenceThresholdSummary> ConfidenceThresholds { get; init; } = [];
    public Dictionary<string, int> ResponseTypes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> DiagnosticKinds { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> SelectedTools { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int FallbackCases { get; init; }
}

public sealed class RunSummary
{
    public int TotalCases { get; init; }
    public List<ConfiguredProviderInfo> ConfiguredProviders { get; init; } = [];
    public Dictionary<string, ProviderSummaryReport> Providers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CommandLineOptions
{
    public IReadOnlyList<int>? SelectedOrdinals { get; init; }
    public IReadOnlyList<string>? SelectedProviderKeys { get; init; }
    public string? BaselineSummaryPath { get; init; }
    public BaselineComparisonMetrics ComparisonMetrics { get; init; }
    public string? FreezeBaselinePath { get; init; }
    public string? TrainingSourcePath { get; init; }
    public string? TrainingOutputPath { get; init; }
}

public sealed class BaselineManifest
{
    public string BaselineId { get; init; } = string.Empty;
    public string ProviderKey { get; init; } = string.Empty;
    public string ProviderDisplayName { get; init; } = string.Empty;
    public string DatasetPath { get; init; } = string.Empty;
    public string? CreatedUtc { get; init; }
    public string? SourceRunDirectory { get; init; }
    public string? Command { get; init; }
    public int? TotalCases { get; init; }
    public double? IntentAccuracy { get; init; }
    public double? ParameterAccuracy { get; init; }
    public double? AverageConfidence { get; init; }
    public double? AverageDurationMs { get; init; }
    public decimal? TotalCostUsd { get; init; }
    public List<string> Notes { get; init; } = [];
}

public sealed class LoadedBaseline
{
    public string SummaryPath { get; init; } = string.Empty;
    public BaselineManifest? Manifest { get; init; }
    public RunSummary Summary { get; init; } = new();
    public string ProviderKey { get; init; } = string.Empty;
    public string ProviderDisplayName { get; init; } = string.Empty;
}

public sealed class MetricDelta
{
    public double? Current { get; init; }
    public double? Baseline { get; init; }
    public double? Delta { get; init; }
}

public sealed class PerIntentComparison
{
    public MetricDelta IntentAccuracy { get; init; } = new();
    public MetricDelta ParameterAccuracy { get; init; } = new();
}

public sealed class ProviderComparisonReport
{
    public string ProviderKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string BaselineProviderKey { get; init; } = string.Empty;
    public string BaselineDisplayName { get; init; } = string.Empty;
    public int ErrorCases { get; init; }
    public int BaselineErrorCases { get; init; }
    public int FallbackCases { get; init; }
    public int BaselineFallbackCases { get; init; }
    public MetricDelta IntentAccuracy { get; init; } = new();
    public MetricDelta ParameterAccuracy { get; init; } = new();
    public MetricDelta AverageConfidence { get; init; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MetricDelta? AverageDurationMs { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MetricDelta? AverageCostUsd { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MetricDelta? TotalCostUsd { get; init; }
    public Dictionary<string, PerIntentComparison> PerIntent { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BaselineComparisonReport
{
    public string? BaselineId { get; init; }
    public string BaselineSummaryPath { get; init; } = string.Empty;
    public string BaselineProviderKey { get; init; } = string.Empty;
    public string BaselineProviderDisplayName { get; init; } = string.Empty;
    public int CurrentTotalCases { get; init; }
    public int BaselineTotalCases { get; init; }
    public bool TotalCasesMatch { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? IncludedMetrics { get; init; }
    public Dictionary<string, ProviderComparisonReport> Providers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class Program
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly double[] ConfidenceThresholds = [0.25, 0.50, 0.75, 0.90];

    public static async Task Main(string[] args)
    {
        var appBaseDir = AppContext.BaseDirectory;
        var projectRoot = HarnessPathUtils.GetProjectRoot(appBaseDir);
        var repositoryRoot = HarnessPathUtils.GetRepositoryRoot(projectRoot);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(projectRoot)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        NeedleIntentClient.Configure(configuration["Needle:NativeLibraryPath"]);
        repositoryRoot = HarnessPathUtils.GetRepositoryRoot(projectRoot, configuration["Harness:RepositoryRoot"]);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        var datasetPath = Path.Combine(projectRoot, "Dataset", "intent-eval-dataset.json");
        var dataset = JsonSerializer.Deserialize<List<IntentEvalCase>>(
            File.ReadAllText(datasetPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

        Console.WriteLine($"Loaded {dataset.Count} intent-eval cases from {datasetPath}");

        if (args.Any(arg => arg is "--help" or "-h" or "/?"))
        {
            PrintUsage();
            return;
        }

        CommandLineOptions options;
        try
        {
            options = ParseCommandLineOptions(args, dataset.Count);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            PrintUsage();
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.TrainingSourcePath))
        {
            try
            {
                var sourcePath = ResolveInputPath(projectRoot, repositoryRoot, options.TrainingSourcePath, Path.GetFileName(options.TrainingSourcePath));
                var outputPath = ResolveOutputPath(projectRoot, repositoryRoot, options.TrainingOutputPath, sourcePath);
                var exportResult = TrainingDatasetExporter.ExportJsonl(projectRoot, sourcePath, outputPath);

                Console.WriteLine($"Training JSONL exported from {HarnessPathUtils.ToProjectRelative(projectRoot, sourcePath)}");
                Console.WriteLine($"JSONL path: {exportResult.JsonlPath}");
                Console.WriteLine($"Summary path: {exportResult.SummaryPath}");
                Console.WriteLine($"Examples exported: {exportResult.TotalExamples}");
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
            }

            return;
        }

        LoadedBaseline? baseline = null;
        try
        {
            baseline = LoadBaseline(projectRoot, repositoryRoot, options.BaselineSummaryPath);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return;
        }

        var selectedOrdinals = options.SelectedOrdinals;
        var selectedCases = selectedOrdinals is null
            ? dataset.Select((testCase, index) => (Ordinal: index + 1, Case: testCase)).ToList()
            : selectedOrdinals.Select(ordinal => (Ordinal: ordinal, Case: dataset[ordinal - 1])).ToList();

        if (selectedOrdinals is null)
        {
            Console.WriteLine("Running all cases.");
        }
        else
        {
            Console.WriteLine($"Running {selectedCases.Count} selected case(s): {string.Join(", ", selectedOrdinals)}");
        }

        var allProviders = BuildProviders(projectRoot, repositoryRoot, configuration, loggerFactory);
        List<EvalProvider> providers;

        try
        {
            providers = SelectProviders(allProviders, options.SelectedProviderKeys);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.FreezeBaselinePath) && providers.Count != 1)
        {
            Console.Error.WriteLine("ERROR: --freeze-baseline requires exactly one selected provider.");
            return;
        }

        Console.WriteLine($"Running {providers.Count} provider(s): {string.Join(", ", providers.Select(provider => provider.Key))}");

        var rows = new List<EvalRow>();

        foreach (var (ordinal, testCase) in selectedCases)
        {
            rows.Add(new EvalRow
            {
                Ordinal = ordinal,
                Case = testCase,
                Outcomes = new Dictionary<string, ProviderOutcome>(StringComparer.OrdinalIgnoreCase)
            });
        }

        foreach (var provider in providers)
        {
            await RunProviderPassAsync(provider, rows);
        }

        var runId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var outDir = Path.Combine(repositoryRoot, "artifacts", "eval", $"run_{runId}");
        Directory.CreateDirectory(outDir);

        WriteCsv(Path.Combine(outDir, "results.csv"), providers, rows);
            WriteNeedleDiagnostics(Path.Combine(outDir, "needle_diagnostics.json"), providers, rows);
        var summary = WriteSummary(Path.Combine(outDir, "summary.json"), providers, rows);
        if (baseline is not null)
        {
            var comparisonPath = Path.Combine(outDir, "baseline_comparison.json");
            var comparison = WriteBaselineComparison(comparisonPath, summary, baseline, options.ComparisonMetrics);
            Console.WriteLine($"Baseline comparison written to {HarnessPathUtils.ToProjectRelative(projectRoot, comparisonPath)}");
            if (options.ComparisonMetrics != BaselineComparisonMetrics.None)
            {
                PrintBaselineComparisonSummary(comparison, options.ComparisonMetrics);
            }
        }

        if (!string.IsNullOrWhiteSpace(options.FreezeBaselinePath))
        {
            var baselineDirectory = FreezeBaselineArtifacts(
                projectRoot,
                datasetPath,
                outDir,
                summary,
                providers,
                options.FreezeBaselinePath,
                args);
            Console.WriteLine($"Baseline frozen to {HarnessPathUtils.ToProjectRelative(projectRoot, baselineDirectory)}");
        }

        PrintSummary(summary);
        Console.WriteLine($"Results written to {HarnessPathUtils.ToProjectRelative(projectRoot, outDir)}");
    }

    private static List<EvalProvider> BuildProviders(string projectRoot, string repositoryRoot, IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        var openAiService = TryCreateOpenAiService(configuration, loggerFactory);
        var weightRegistry = new NeedleWeightRegistry(projectRoot, configuration);

        NeedleIntentClient? baseNeedleClient = null;

        NeedleIntentClient? tunedNeedleClient = null;
        var tunedArtifact = weightRegistry.GetPreferredTunedArtifact();

        var providers = new List<EvalProvider>
        {
            new EvalProvider(
                key: "openAi",
                displayName: "OpenAI",
                enabled: openAiService is not null,
                evaluateAsync: testCase => EvaluateOpenAiAsync(openAiService, testCase)),
            new EvalProvider(
                key: "needleBase",
                displayName: "Needle Base",
                enabled: true,
                evaluateAsync: testCase => Task.FromResult(EvaluateNeedle(baseNeedleClient, testCase, "needleBase")),
                initializeAsync: () =>
                {
                    baseNeedleClient = TryCreateNeedleClient();
                    return Task.CompletedTask;
                },
                disposeAction: () =>
                {
                    baseNeedleClient?.Dispose();
                    baseNeedleClient = null;
                })
        };

        if (tunedArtifact is not null)
        {
            providers.Add(
                new EvalProvider(
                    key: tunedArtifact.ProviderKey,
                    displayName: tunedArtifact.DisplayName,
                    enabled: true,
                    evaluateAsync: testCase => Task.FromResult(EvaluateNeedle(tunedNeedleClient, testCase, tunedArtifact.ProviderKey)),
                    initializeAsync: () =>
                    {
                        weightRegistry.Validate(tunedArtifact);
                        Console.WriteLine($"Activating tuned Needle weights: {weightRegistry.Describe(tunedArtifact)}");
                        tunedNeedleClient = new NeedleIntentClient(tunedArtifact);
                        return Task.CompletedTask;
                    },
                    disposeAction: () =>
                    {
                        tunedNeedleClient?.Dispose();
                        tunedNeedleClient = null;
                    }));
        }

        NeedleIntentClient? v3NeedleClient = null;
        var v3Artifact = BuildV3Artifact(projectRoot, repositoryRoot, configuration);
        if (v3Artifact is not null)
        {
            providers.Add(
                new EvalProvider(
                    key: v3Artifact.ProviderKey,
                    displayName: v3Artifact.DisplayName,
                    enabled: true,
                    evaluateAsync: testCase => Task.FromResult(EvaluateNeedle(v3NeedleClient, testCase, v3Artifact.ProviderKey)),
                    initializeAsync: () =>
                    {
                        v3NeedleClient = TryCreateNeedleV3Client(repositoryRoot, configuration, v3Artifact);
                        return Task.CompletedTask;
                    },
                    disposeAction: () =>
                    {
                        v3NeedleClient?.Dispose();
                        v3NeedleClient = null;
                    }));
        }

        return providers;
    }

    private static NeedleWeightArtifact? BuildV3Artifact(string projectRoot, string repositoryRoot, IConfiguration configuration)
    {
        var configuredPath = configuration["Needle:V3WeightsPath"];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        string resolvedPath;
        if (Path.IsPathRooted(configuredPath))
        {
            resolvedPath = configuredPath;
        }
        else
        {
            var candidates = new[]
            {
                Path.GetFullPath(Path.Combine(projectRoot, configuredPath)),
                Path.GetFullPath(Path.Combine(repositoryRoot, configuredPath)),
                Path.GetFullPath(Path.Combine(repositoryRoot, "IntentEvalHarness", configuredPath))
            };
            resolvedPath = candidates.FirstOrDefault(File.Exists) ?? candidates[0];
        }

        var displayName = configuration["Needle:V3WeightsDisplayName"];
        var fileExists = File.Exists(resolvedPath);

        return new NeedleWeightArtifact
        {
            ProviderKey = "needleV3",
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Needle 3 Base" : displayName,
            WeightsPath = resolvedPath,
            Sha256 = configuration["Needle:V3WeightsSha256"],
            FileExists = fileExists,
            FileSizeBytes = fileExists ? new FileInfo(resolvedPath).Length : null
        };
    }

    private static NeedleIntentClient? TryCreateNeedleV3Client(string repositoryRoot, IConfiguration configuration, NeedleWeightArtifact artifact)
    {
        try
        {
            var configuredNativePath = configuration["Needle:V3NativeLibraryPath"];
            var nativePath = string.IsNullOrWhiteSpace(configuredNativePath)
                ? Path.Combine(repositoryRoot, "IntentEvalHarness", "native", NeedleIntentClient.GetCurrentRid(), "3.0.2", NeedleIntentClient.GetNativeLibraryFileName())
                : configuredNativePath;

            NeedleIntentClient.Configure(nativePath);
            Console.WriteLine($"Activating Needle3 weights: {artifact.DisplayName} [{artifact.WeightsPath}]");
            return new NeedleIntentClient(artifact);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: Skipping Needle3 provider — {ex.Message}");
            return null;
        }
    }

    private static async Task RunProviderPassAsync(EvalProvider provider, List<EvalRow> rows)
    {
        Console.WriteLine();
        Console.WriteLine($"=== Provider: {provider.DisplayName} ===");

        try
        {
            if (provider.InitializeAsync is not null)
            {
                await provider.InitializeAsync();
            }

            foreach (var row in rows)
            {
                Console.WriteLine($"Evaluating case {row.Ordinal} with {provider.DisplayName}: \"{row.Case.Input}\"");
                var stopwatch = Stopwatch.StartNew();
                var outcome = await provider.EvaluateAsync(row.Case);
                stopwatch.Stop();
                outcome.DurationMs = (int)stopwatch.ElapsedMilliseconds;
                row.Outcomes[provider.Key] = outcome;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: Provider '{provider.DisplayName}' failed to initialize or run — {ex.Message}");
            foreach (var row in rows)
            {
                row.Outcomes[provider.Key] = new ProviderOutcome
                {
                    Error = $"Provider run failed: {ex.Message}"
                };
            }
        }
        finally
        {
            provider.DisposeAction?.Invoke();
        }
    }

    internal static CommandLineOptions ParseCommandLineOptions(string[] args, int totalCases)
    {
        string? caseSpec = null;
        string? providerSpec = null;
        string? baselineSpec = null;
        string? compareMetricsSpec = null;
        string? freezeBaselineSpec = null;
        string? trainingSourceSpec = null;
        string? trainingOutputSpec = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is "--help" or "-h" or "/?")
            {
                continue;
            }

            if (arg is "--cases" or "-c")
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("Missing value for --cases.");
                }

                if (caseSpec is not null)
                {
                    throw new ArgumentException("Specify case ordinals only once.");
                }

                caseSpec = args[++index];
                continue;
            }

            if (arg is "--providers" or "-p")
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("Missing value for --providers.");
                }

                if (providerSpec is not null)
                {
                    throw new ArgumentException("Specify providers only once.");
                }

                providerSpec = args[++index];
                continue;
            }

            if (arg == "--compare-baseline")
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("Missing value for --compare-baseline.");
                }

                if (baselineSpec is not null)
                {
                    throw new ArgumentException("Specify --compare-baseline only once.");
                }

                baselineSpec = args[++index];
                continue;
            }

            if (arg == "--compare-metrics")
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("Missing value for --compare-metrics.");
                }

                if (compareMetricsSpec is not null)
                {
                    throw new ArgumentException("Specify --compare-metrics only once.");
                }

                compareMetricsSpec = args[++index];
                continue;
            }

            if (arg == "--freeze-baseline")
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("Missing value for --freeze-baseline.");
                }

                if (freezeBaselineSpec is not null)
                {
                    throw new ArgumentException("Specify --freeze-baseline only once.");
                }

                freezeBaselineSpec = args[++index];
                continue;
            }

            if (arg == "--export-training-jsonl")
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("Missing value for --export-training-jsonl.");
                }

                if (trainingSourceSpec is not null)
                {
                    throw new ArgumentException("Specify --export-training-jsonl only once.");
                }

                trainingSourceSpec = args[++index];
                continue;
            }

            if (arg == "--output")
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("Missing value for --output.");
                }

                if (trainingOutputSpec is not null)
                {
                    throw new ArgumentException("Specify --output only once.");
                }

                trainingOutputSpec = args[++index];
                continue;
            }

            if (arg.StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unknown option '{arg}'.");
            }

            if (caseSpec is not null)
            {
                throw new ArgumentException("Specify case ordinals either as a single positional argument or with --cases.");
            }

            caseSpec = arg;
        }

        if (caseSpec is not null && string.IsNullOrWhiteSpace(caseSpec))
        {
            throw new ArgumentException("Case selection cannot be empty.");
        }

        if (trainingOutputSpec is not null && trainingSourceSpec is null)
        {
            throw new ArgumentException("--output can only be used with --export-training-jsonl.");
        }

        if (freezeBaselineSpec is not null && trainingSourceSpec is not null)
        {
            throw new ArgumentException("--freeze-baseline cannot be used with --export-training-jsonl.");
        }

        var comparisonMetrics = compareMetricsSpec is null
            ? BaselineComparisonMetrics.None
            : ParseComparisonMetrics(compareMetricsSpec);

        if (comparisonMetrics != BaselineComparisonMetrics.None && baselineSpec is null)
        {
            throw new ArgumentException("--compare-metrics requires --compare-baseline.");
        }

        return new CommandLineOptions
        {
            SelectedOrdinals = caseSpec is null ? null : ParseOrdinalSpec(caseSpec, totalCases),
            SelectedProviderKeys = ParseProviderSpec(providerSpec),
            BaselineSummaryPath = baselineSpec,
            ComparisonMetrics = comparisonMetrics,
            FreezeBaselinePath = freezeBaselineSpec,
            TrainingSourcePath = trainingSourceSpec,
            TrainingOutputPath = trainingOutputSpec
        };
    }

    internal static BaselineComparisonMetrics ParseComparisonMetrics(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            throw new ArgumentException("Comparison metrics cannot be empty.");
        }

        var metrics = BaselineComparisonMetrics.None;
        foreach (var rawToken in spec.Split(','))
        {
            var token = rawToken.Trim();
            if (token.Length == 0)
            {
                throw new ArgumentException($"Invalid comparison metric list '{spec}'. Empty entries are not allowed.");
            }

            metrics |= token.ToLowerInvariant() switch
            {
                "runtime" => BaselineComparisonMetrics.Runtime,
                "cost" => BaselineComparisonMetrics.Cost,
                _ => throw new ArgumentException($"Unknown comparison metric '{token}'. Supported metrics: runtime, cost.")
            };
        }

        return metrics;
    }

    internal static LoadedBaseline? LoadBaseline(string projectRoot, string repositoryRoot, string? baselineSpec)
    {
        if (string.IsNullOrWhiteSpace(baselineSpec))
        {
            return null;
        }

        var summaryPath = ResolveInputPath(projectRoot, repositoryRoot, baselineSpec, "summary.json");
        if (!File.Exists(summaryPath))
        {
            throw new ArgumentException($"Baseline summary was not found at '{baselineSpec}'. Resolved path: {summaryPath}");
        }

        var summary = JsonSerializer.Deserialize<RunSummary>(
            File.ReadAllText(summaryPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (summary is null)
        {
            throw new ArgumentException($"Baseline summary at '{summaryPath}' could not be parsed.");
        }

        var manifestPath = Path.Combine(Path.GetDirectoryName(summaryPath) ?? string.Empty, "manifest.json");
        BaselineManifest? manifest = null;
        if (File.Exists(manifestPath))
        {
            manifest = JsonSerializer.Deserialize<BaselineManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        var providerKey = !string.IsNullOrWhiteSpace(manifest?.ProviderKey)
            ? manifest.ProviderKey
            : summary.Providers.Keys.SingleOrDefault() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(providerKey) || !summary.Providers.TryGetValue(providerKey, out var providerSummary))
        {
            throw new ArgumentException(
                $"Baseline summary at '{summaryPath}' must contain exactly one provider or a manifest.json with providerKey.");
        }

        return new LoadedBaseline
        {
            SummaryPath = HarnessPathUtils.ToProjectRelative(projectRoot, summaryPath),
            Manifest = manifest,
            Summary = summary,
            ProviderKey = providerKey,
            ProviderDisplayName = string.IsNullOrWhiteSpace(manifest?.ProviderDisplayName)
                ? providerSummary.DisplayName
                : manifest.ProviderDisplayName
        };
    }

    internal static string ResolveInputPath(string projectRoot, string repositoryRoot, string requestedPath, string fallbackFileName)
    {
        var normalizedPath = NormalizeRelativePath(requestedPath);

        if (Path.IsPathRooted(requestedPath))
        {
            return Directory.Exists(requestedPath)
                ? Path.Combine(requestedPath, fallbackFileName)
                : requestedPath;
        }

        var preferredBase = IsRepositoryRelativePath(normalizedPath)
            ? repositoryRoot
            : projectRoot;

        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(preferredBase, normalizedPath)),
            Path.GetFullPath(Path.Combine(projectRoot, normalizedPath)),
            Path.GetFullPath(Path.Combine(repositoryRoot, normalizedPath))
        }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return Path.Combine(candidate, fallbackFileName);
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var firstCandidate = candidates[0];
        return Path.HasExtension(firstCandidate)
            ? firstCandidate
            : Path.Combine(firstCandidate, fallbackFileName);
    }

    internal static string ResolveOutputPath(string projectRoot, string repositoryRoot, string? requestedOutputPath, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(requestedOutputPath))
        {
            var sourceDirectory = Path.GetDirectoryName(sourcePath) ?? projectRoot;
            var sourceFileName = Path.GetFileNameWithoutExtension(sourcePath);
            return Path.Combine(sourceDirectory, $"{sourceFileName}.jsonl");
        }

        if (Path.IsPathRooted(requestedOutputPath))
        {
            return requestedOutputPath;
        }

        var normalizedPath = NormalizeRelativePath(requestedOutputPath);
        var preferredBase = IsRepositoryRelativePath(normalizedPath)
            ? repositoryRoot
            : projectRoot;

        return Path.GetFullPath(Path.Combine(preferredBase, normalizedPath));
    }

    private static string NormalizeRelativePath(string path)
    {
        return path
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
    }

    private static bool IsRepositoryRelativePath(string normalizedPath)
    {
        if (normalizedPath.StartsWith($"IntentEvalHarness{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(normalizedPath, "artifacts", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedPath.StartsWith($"artifacts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<string>? ParseProviderSpec(string? spec)
    {
        if (spec is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(spec))
        {
            throw new ArgumentException("Provider selection cannot be empty.");
        }

        var providers = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawToken in spec.Split(','))
        {
            var token = rawToken.Trim();
            if (token.Length == 0)
            {
                throw new ArgumentException($"Invalid provider selection '{spec}'. Empty entries are not allowed.");
            }

            if (seen.Add(token))
            {
                providers.Add(token);
            }
        }

        if (providers.Count == 0)
        {
            throw new ArgumentException("Provider selection cannot be empty.");
        }

        return providers;
    }

    internal static List<EvalProvider> SelectProviders(List<EvalProvider> allProviders, IReadOnlyList<string>? requestedProviderKeys)
    {
        if (requestedProviderKeys is null)
        {
            var enabledProviders = allProviders.Where(provider => provider.Enabled).ToList();
            return enabledProviders.Count > 0 ? enabledProviders : allProviders;
        }

        var requested = new HashSet<string>(requestedProviderKeys, StringComparer.OrdinalIgnoreCase);
        var selected = allProviders
            .Where(provider => requested.Contains(provider.Key))
            .ToList();

        var selectedKeys = new HashSet<string>(selected.Select(provider => provider.Key), StringComparer.OrdinalIgnoreCase);
        var missingKeys = requestedProviderKeys
            .Where(key => !selectedKeys.Contains(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missingKeys.Count > 0)
        {
            throw new ArgumentException(
                $"Unknown provider key(s): {string.Join(", ", missingKeys)}. Available providers: {string.Join(", ", allProviders.Select(provider => provider.Key))}.");
        }

        var disabledProviders = selected
            .Where(provider => !provider.Enabled)
            .Select(provider => provider.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (disabledProviders.Count > 0)
        {
            throw new ArgumentException(
                $"Provider(s) disabled by configuration: {string.Join(", ", disabledProviders)}. " +
                "For OpenAI, set OpenAI:Enabled=true, OpenAI:AllowLiveCalls=true, and OpenAI:ApiKey.");
        }

        var selectedNeedleKeys = selected
            .Select(provider => provider.Key)
            .Where(key => key.StartsWith("needle", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (selectedNeedleKeys.Any(key => string.Equals(key, "needleV3", StringComparison.OrdinalIgnoreCase)) &&
            selectedNeedleKeys.Any(key => !string.Equals(key, "needleV3", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "needleV3 (Needle3 engine) cannot be selected together with needleBase or a tuned Needle2 key in one run. " +
                "The Needle2 and Needle3 native engines cannot be loaded in the same process; run them in separate invocations.");
        }

        return selected;
    }

    internal static IReadOnlyList<int> ParseOrdinalSpec(string spec, int totalCases)
    {
        var ordinals = new List<int>();
        var seen = new HashSet<int>();

        foreach (var rawToken in spec.Split(','))
        {
            var token = rawToken.Trim();
            if (token.Length == 0)
            {
                throw new ArgumentException($"Invalid case selection '{spec}'. Empty entries are not allowed.");
            }

            var parts = token.Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length == 1)
            {
                AddOrdinal(ParseOrdinal(parts[0], token, totalCases));
                continue;
            }

            if (parts.Length != 2)
            {
                throw new ArgumentException($"Invalid case range '{token}'. Use values like 5 or 5-10.");
            }

            var start = ParseOrdinal(parts[0], token, totalCases);
            var end = ParseOrdinal(parts[1], token, totalCases);
            if (end < start)
            {
                throw new ArgumentException($"Invalid case range '{token}'. Range end must be greater than or equal to the start.");
            }

            for (var ordinal = start; ordinal <= end; ordinal++)
            {
                AddOrdinal(ordinal);
            }
        }

        if (ordinals.Count == 0)
        {
            throw new ArgumentException("Case selection cannot be empty.");
        }

        return ordinals;

        void AddOrdinal(int ordinal)
        {
            if (seen.Add(ordinal))
            {
                ordinals.Add(ordinal);
            }
        }
    }

    internal static int ParseOrdinal(string value, string token, int totalCases)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal) || ordinal < 1)
        {
            throw new ArgumentException($"Invalid case ordinal '{value}' in '{token}'. Use positive 1-based ordinals.");
        }

        if (ordinal > totalCases)
        {
            throw new ArgumentException($"Case ordinal {ordinal} is out of range. Dataset contains {totalCases} case(s).");
        }

        return ordinal;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project IntentEvalHarness");
        Console.WriteLine("  dotnet run --project IntentEvalHarness -- 1,5-10,19,20");
        Console.WriteLine("  dotnet run --project IntentEvalHarness -- --cases 1,5-10,19,20");
        Console.WriteLine("  dotnet run --project IntentEvalHarness -- --providers needleBase");
        Console.WriteLine("  dotnet run --project IntentEvalHarness -- --providers needleBase 1,5-10,19,20");
        Console.WriteLine("  dotnet run --project IntentEvalHarness -- --providers needleBase --compare-baseline Baselines/openAi_frozen_20260823_full45");
        Console.WriteLine("  dotnet run --project IntentEvalHarness -- --providers needleBase --compare-baseline Baselines/openAi_frozen_20260823_full45 --compare-metrics runtime,cost");
        Console.WriteLine("  dotnet run --project IntentEvalHarness -- --providers openAi --freeze-baseline openAi_frozen_20260904_full45");
        Console.WriteLine("  dotnet run --project IntentEvalHarness -- --providers needleV3");
        Console.WriteLine("  dotnet run --project IntentEvalHarness -- --export-training-jsonl Dataset/needle-training-seed.json --output Dataset/training_set.seed.jsonl");
        Console.WriteLine("Provider keys:");
        Console.WriteLine("  openAi, needleBase, needleV3, and any discovered tuned Needle2 key such as needleMyTunedRun");
        Console.WriteLine("  needleV3 (Needle3 engine) cannot be selected together with needleBase or a tuned Needle2 key in one run: the native engines are mutually exclusive within one process.");
    }

    private static OpenAiIntentEvaluator? TryCreateOpenAiService(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        var openAiEnabled = configuration.GetValue("OpenAI:Enabled", false);
        if (!openAiEnabled)
        {
            Console.WriteLine("INFO: OpenAI provider disabled (OpenAI:Enabled=false).");
            return null;
        }

        var allowLiveCalls = configuration.GetValue("OpenAI:AllowLiveCalls", false);
        if (!allowLiveCalls)
        {
            Console.WriteLine("INFO: OpenAI provider disabled (OpenAI:AllowLiveCalls=false).");
            return null;
        }

        var apiKey = configuration["OpenAI:ApiKey"] ?? configuration["IntentEvaluationService:ApiKey"] ?? configuration["VisionServiceApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.WriteLine("WARNING: Skipping OpenAI provider — OpenAI:ApiKey was not supplied.");
            return null;
        }

        try
        {
            return new OpenAiIntentEvaluator(
                configuration,
                loggerFactory.CreateLogger<OpenAiIntentEvaluator>(),
                apiKey);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: Skipping OpenAI provider — {ex.Message}");
            return null;
        }
    }

    private static NeedleIntentClient? TryCreateNeedleClient()
    {
        try
        {
            return new NeedleIntentClient();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: Skipping Needle2 provider — {ex.Message}");
            return null;
        }
    }

    private static async Task<ProviderOutcome> EvaluateOpenAiAsync(OpenAiIntentEvaluator? service, IntentEvalCase testCase)
    {
        if (service is null)
        {
            return new ProviderOutcome { Error = "Provider not configured" };
        }

        try
        {
            var response = await service.EvaluateIntentDetailedAsync(testCase.Input);
            if (!response.Success || response.Data is null)
            {
                return new ProviderOutcome { Error = response.Message };
            }

            return BuildOpenAiOutcome(testCase, response.Data);
        }
        catch (Exception ex)
        {
            return new ProviderOutcome { Error = ex.Message };
        }
    }

    private static ProviderOutcome EvaluateNeedle(NeedleIntentClient? client, IntentEvalCase testCase, string providerKey)
    {
        if (client is null)
        {
            return new ProviderOutcome { Error = "Provider not configured" };
        }

        try
        {
            // Each dataset case is an independent single-turn query; reset conversation/KV-cache state
            // beforehand so context doesn't grow unbounded across the whole run.
            client.Reset();
            var completion = client.CompleteWithDiagnostics(testCase.Input);
            return BuildNeedleOutcome(testCase, completion.Result, completion, providerKey);
        }
        catch (Exception ex)
        {
            return new ProviderOutcome { Error = ex.Message };
        }
    }

    private static ProviderOutcome BuildOpenAiOutcome(IntentEvalCase testCase, IntentEvaluationExecutionResult execution)
    {
        var outcome = BuildOutcome(testCase, execution.Evaluation);
        outcome.InputTokens = execution.InputTokens;
        outcome.OutputTokens = execution.OutputTokens;
        outcome.TokensEstimated = execution.TokensEstimated;
        outcome.CostUsd = execution.CostUsd;
        outcome.CostSource = execution.CacheHit ? "cache-hit" : "configured-pricing";
        outcome.ModelName = execution.ModelName;
        return outcome;
    }

    private static ProviderOutcome BuildNeedleOutcome(IntentEvalCase testCase, IntentEvaluationResult result, NeedleIntentCompletion completion, string providerKey)
    {
        var outcome = BuildOutcome(testCase, result, completion);
        outcome.CostUsd = 0m;
        outcome.CostSource = "free";
        outcome.ModelName = providerKey;
        return outcome;
    }

    internal static ProviderOutcome BuildOutcome(IntentEvalCase testCase, IntentEvaluationResult result)
    {
        return BuildOutcome(testCase, result, null);
    }

    internal static ProviderOutcome BuildOutcome(IntentEvalCase testCase, IntentEvaluationResult result, NeedleIntentCompletion? completion)
    {
        var intentMatch = string.Equals(result.Intent.Trim(), testCase.ExpectedIntent.Trim(), StringComparison.OrdinalIgnoreCase);

        var actualParameters = new Dictionary<string, object>(result.Parameters, StringComparer.OrdinalIgnoreCase);
        var paramMatch = true;
        foreach (var (key, expectedValue) in testCase.ExpectedParameters)
        {
            if (!actualParameters.TryGetValue(key, out var actualValue) ||
                !string.Equals(NormalizeParamValue(actualValue), NormalizeParamValue(expectedValue), StringComparison.OrdinalIgnoreCase))
            {
                paramMatch = false;
                break;
            }
        }

        return new ProviderOutcome
        {
            Intent = result.Intent,
            Confidence = completion is null ? result.Confidence : completion.ReportedConfidence,
            IntentMatch = intentMatch,
            ParamMatch = paramMatch,
            ResponseType = completion?.ResponseType,
            ToolName = completion?.FunctionName,
            DiagnosticKind = completion?.DiagnosticKind,
            FallbackReason = completion?.FallbackReason,
            RawArgumentsJson = completion?.ArgumentsJson,
            RawResponseJson = completion?.RawResponseJson,
            ModelName = completion is null ? null : "needle"
        };
    }

    internal static string NormalizeParamValue(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is JsonElement element)
        {
            var raw = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                _ => element.GetRawText()
            };
            return raw.Trim().ToLowerInvariant();
        }

        return value.ToString()?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    private static void WriteCsv(string path, IReadOnlyList<EvalProvider> providers, List<EvalRow> rows)
    {
        var header = new List<string> { "caseOrdinal", "input", "expectedIntent" };
        foreach (var provider in providers)
        {
            header.Add($"{provider.Key}Intent");
            header.Add($"{provider.Key}Confidence");
            header.Add($"{provider.Key}IntentMatch");
            header.Add($"{provider.Key}ParamMatch");
            header.Add($"{provider.Key}Error");
            header.Add($"{provider.Key}ResponseType");
            header.Add($"{provider.Key}ToolName");
            header.Add($"{provider.Key}DiagnosticKind");
            header.Add($"{provider.Key}FallbackReason");
            header.Add($"{provider.Key}DurationMs");
            header.Add($"{provider.Key}InputTokens");
            header.Add($"{provider.Key}OutputTokens");
            header.Add($"{provider.Key}TokensEstimated");
            header.Add($"{provider.Key}CostUsd");
            header.Add($"{provider.Key}CostSource");
            header.Add($"{provider.Key}ModelName");
        }

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", header));

        foreach (var row in rows)
        {
            var values = new List<string>
            {
                row.Ordinal.ToString(CultureInfo.InvariantCulture),
                CsvEscape(row.Case.Input),
                CsvEscape(row.Case.ExpectedIntent)
            };

            foreach (var provider in providers)
            {
                var outcome = GetOutcome(row, provider.Key);
                values.Add(CsvEscape(outcome.Intent));
                values.Add(CsvEscape(outcome.Confidence?.ToString(CultureInfo.InvariantCulture)));
                values.Add(outcome.IntentMatch.ToString(CultureInfo.InvariantCulture));
                values.Add(outcome.ParamMatch.ToString(CultureInfo.InvariantCulture));
                values.Add(CsvEscape(outcome.Error));
                values.Add(CsvEscape(outcome.ResponseType));
                values.Add(CsvEscape(outcome.ToolName));
                values.Add(CsvEscape(outcome.DiagnosticKind));
                values.Add(CsvEscape(outcome.FallbackReason));
                values.Add(CsvEscape(outcome.DurationMs?.ToString(CultureInfo.InvariantCulture)));
                values.Add(CsvEscape(outcome.InputTokens?.ToString(CultureInfo.InvariantCulture)));
                values.Add(CsvEscape(outcome.OutputTokens?.ToString(CultureInfo.InvariantCulture)));
                values.Add(CsvEscape(outcome.TokensEstimated?.ToString()));
                values.Add(CsvEscape(outcome.CostUsd?.ToString(CultureInfo.InvariantCulture)));
                values.Add(CsvEscape(outcome.CostSource));
                values.Add(CsvEscape(outcome.ModelName));
            }

            sb.AppendLine(string.Join(",", values));
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void WriteNeedleDiagnostics(string path, IReadOnlyList<EvalProvider> providers, List<EvalRow> rows)
    {
        var diagnosticProviders = providers
            .Where(provider => rows.Any(row => HasDiagnosticData(GetOutcome(row, provider.Key))))
            .ToList();

        var diagnostics = rows
            .Select(row => new
            {
                caseOrdinal = row.Ordinal,
                input = row.Case.Input,
                expectedIntent = row.Case.ExpectedIntent,
                expectedParameters = row.Case.ExpectedParameters,
                notes = row.Case.Notes,
                providers = diagnosticProviders
                    .Select(provider => (Provider: provider, Outcome: GetOutcome(row, provider.Key)))
                    .Where(entry => HasDiagnosticData(entry.Outcome))
                    .ToDictionary(
                        entry => entry.Provider.Key,
                        entry => new
                        {
                            displayName = entry.Provider.DisplayName,
                            intent = entry.Outcome.Intent,
                            confidence = entry.Outcome.Confidence,
                            intentMatch = entry.Outcome.IntentMatch,
                            paramMatch = entry.Outcome.ParamMatch,
                            error = entry.Outcome.Error,
                            responseType = entry.Outcome.ResponseType,
                            toolName = entry.Outcome.ToolName,
                            diagnosticKind = entry.Outcome.DiagnosticKind,
                            fallbackReason = entry.Outcome.FallbackReason,
                            rawArgumentsJson = entry.Outcome.RawArgumentsJson,
                            rawResponseJson = entry.Outcome.RawResponseJson,
                            durationMs = entry.Outcome.DurationMs,
                            inputTokens = entry.Outcome.InputTokens,
                            outputTokens = entry.Outcome.OutputTokens,
                            tokensEstimated = entry.Outcome.TokensEstimated,
                            costUsd = entry.Outcome.CostUsd,
                            costSource = entry.Outcome.CostSource,
                            modelName = entry.Outcome.ModelName
                        },
                        StringComparer.OrdinalIgnoreCase)
            })
            .Where(entry => entry.providers.Count > 0)
            .ToList();

        File.WriteAllText(path, JsonSerializer.Serialize(diagnostics, PrettyJsonOptions));
    }

    private static string CsvEscape(string? value)
    {
        value ??= string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    private static RunSummary WriteSummary(string path, IReadOnlyList<EvalProvider> providers, List<EvalRow> rows)
    {
        var summary = new RunSummary
        {
            TotalCases = rows.Count,
            ConfiguredProviders = providers
                .Select(provider => new ConfiguredProviderInfo
                {
                    Key = provider.Key,
                    DisplayName = provider.DisplayName,
                    Enabled = provider.Enabled
                })
                .ToList(),
            Providers = providers.ToDictionary(
                provider => provider.Key,
                provider => BuildProviderSummary(provider, rows),
                StringComparer.OrdinalIgnoreCase)
        };

        File.WriteAllText(path, JsonSerializer.Serialize(summary, PrettyJsonOptions));
        return summary;
    }

    private static BaselineComparisonReport WriteBaselineComparison(
        string path,
        RunSummary currentSummary,
        LoadedBaseline baseline,
        BaselineComparisonMetrics comparisonMetrics)
    {
        var baselineProvider = baseline.Summary.Providers[baseline.ProviderKey];
        var comparisons = currentSummary.Providers.ToDictionary(
            entry => entry.Key,
            entry => BuildProviderComparison(
                entry.Key,
                entry.Value,
                baseline.ProviderKey,
                baselineProvider,
                baseline.ProviderDisplayName,
                comparisonMetrics),
            StringComparer.OrdinalIgnoreCase);

        var report = new BaselineComparisonReport
        {
            BaselineId = baseline.Manifest?.BaselineId,
            BaselineSummaryPath = baseline.SummaryPath,
            BaselineProviderKey = baseline.ProviderKey,
            BaselineProviderDisplayName = baseline.ProviderDisplayName,
            CurrentTotalCases = currentSummary.TotalCases,
            BaselineTotalCases = baseline.Summary.TotalCases,
            TotalCasesMatch = currentSummary.TotalCases == baseline.Summary.TotalCases,
            IncludedMetrics = GetComparisonMetricLabels(comparisonMetrics),
            Providers = comparisons
        };

        File.WriteAllText(path, JsonSerializer.Serialize(report, PrettyJsonOptions));
        return report;
    }

    internal static ProviderComparisonReport BuildProviderComparison(
        string providerKey,
        ProviderSummaryReport current,
        string baselineProviderKey,
        ProviderSummaryReport baseline,
        string baselineDisplayName,
        BaselineComparisonMetrics comparisonMetrics)
    {
        var intentKeys = current.PerIntent.Keys
            .Union(baseline.PerIntent.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var perIntent = intentKeys.ToDictionary(
            key => key,
            key =>
            {
                var currentIntent = current.PerIntent.TryGetValue(key, out var currentValue)
                    ? currentValue
                    : new IntentBreakdownSummary();
                var baselineIntent = baseline.PerIntent.TryGetValue(key, out var baselineValue)
                    ? baselineValue
                    : new IntentBreakdownSummary();

                return new PerIntentComparison
                {
                    IntentAccuracy = BuildMetricDelta(currentIntent.IntentAccuracy, baselineIntent.IntentAccuracy),
                    ParameterAccuracy = BuildMetricDelta(currentIntent.ParameterAccuracy, baselineIntent.ParameterAccuracy)
                };
            },
            StringComparer.OrdinalIgnoreCase);

        return new ProviderComparisonReport
        {
            ProviderKey = providerKey,
            DisplayName = current.DisplayName,
            BaselineProviderKey = baselineProviderKey,
            BaselineDisplayName = baselineDisplayName,
            ErrorCases = current.ErrorCases,
            BaselineErrorCases = baseline.ErrorCases,
            FallbackCases = current.FallbackCases,
            BaselineFallbackCases = baseline.FallbackCases,
            IntentAccuracy = BuildMetricDelta(current.IntentAccuracy, baseline.IntentAccuracy),
            ParameterAccuracy = BuildMetricDelta(current.ParameterAccuracy, baseline.ParameterAccuracy),
            AverageConfidence = BuildMetricDelta(current.AverageConfidence, baseline.AverageConfidence),
            AverageDurationMs = comparisonMetrics.HasFlag(BaselineComparisonMetrics.Runtime)
                ? BuildMetricDelta(current.AverageDurationMs, baseline.AverageDurationMs)
                : null,
            AverageCostUsd = comparisonMetrics.HasFlag(BaselineComparisonMetrics.Cost)
                ? BuildMetricDelta(current.AverageCostUsd, baseline.AverageCostUsd)
                : null,
            TotalCostUsd = comparisonMetrics.HasFlag(BaselineComparisonMetrics.Cost)
                ? BuildMetricDelta(current.TotalCostUsd, baseline.TotalCostUsd)
                : null,
            PerIntent = perIntent
        };
    }

    private static MetricDelta BuildMetricDelta(double? current, double? baseline)
    {
        return new MetricDelta
        {
            Current = current,
            Baseline = baseline,
            Delta = current.HasValue && baseline.HasValue ? current.Value - baseline.Value : null
        };
    }

    private static MetricDelta BuildMetricDelta(decimal? current, decimal? baseline)
    {
        return new MetricDelta
        {
            Current = current.HasValue ? (double)current.Value : null,
            Baseline = baseline.HasValue ? (double)baseline.Value : null,
            Delta = current.HasValue && baseline.HasValue ? (double)(current.Value - baseline.Value) : null
        };
    }

    private static ProviderSummaryReport BuildProviderSummary(EvalProvider provider, List<EvalRow> rows)
    {
        var outcomes = rows.Select(row => GetOutcome(row, provider.Key)).ToList();
        var evaluated = outcomes.Where(outcome => outcome.Error is null).ToList();
        var errorCount = outcomes.Count - evaluated.Count;
        var confidenceBearing = evaluated.Where(outcome => outcome.Confidence.HasValue).ToList();
        var runtimeBearing = outcomes.Where(outcome => outcome.DurationMs.HasValue).ToList();
        var tokenBearing = outcomes.Where(outcome => outcome.InputTokens.HasValue || outcome.OutputTokens.HasValue).ToList();
        var costBearing = outcomes.Where(outcome => outcome.CostUsd.HasValue).ToList();

        double intentAccuracy = evaluated.Count == 0 ? 0 : evaluated.Count(outcome => outcome.IntentMatch) / (double)evaluated.Count;
        double paramAccuracy = evaluated.Count == 0 ? 0 : evaluated.Count(outcome => outcome.ParamMatch) / (double)evaluated.Count;
        double? averageConfidence = confidenceBearing.Count == 0
            ? null
            : confidenceBearing.Average(outcome => outcome.Confidence ?? 0);
        long? totalDurationMs = runtimeBearing.Count == 0
            ? null
            : runtimeBearing.Sum(outcome => (long)(outcome.DurationMs ?? 0));
        double? averageDurationMs = runtimeBearing.Count == 0
            ? null
            : runtimeBearing.Average(outcome => (double)(outcome.DurationMs ?? 0));
        int? totalInputTokens = tokenBearing.Count == 0
            ? null
            : tokenBearing.Sum(outcome => outcome.InputTokens ?? 0);
        int? totalOutputTokens = tokenBearing.Count == 0
            ? null
            : tokenBearing.Sum(outcome => outcome.OutputTokens ?? 0);
        decimal? totalCostUsd = costBearing.Count == 0
            ? null
            : costBearing.Sum(outcome => outcome.CostUsd ?? 0m);
        decimal? averageCostUsd = costBearing.Count == 0 || totalCostUsd is null
            ? null
            : totalCostUsd.Value / costBearing.Count;

        var perIntent = rows
            .GroupBy(row => row.Case.ExpectedIntent, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => BuildIntentBreakdown(group.ToList(), provider.Key),
                StringComparer.OrdinalIgnoreCase);

        var confusionMatrix = perIntent.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.PredictedIntentCounts,
            StringComparer.OrdinalIgnoreCase);

        var responseTypes = BuildCountDictionary(evaluated.Select(outcome => outcome.ResponseType));
        var diagnosticKinds = BuildCountDictionary(evaluated.Select(outcome => outcome.DiagnosticKind));
        var selectedTools = BuildCountDictionary(evaluated.Select(outcome => outcome.ToolName));

        return new ProviderSummaryReport
        {
            DisplayName = provider.DisplayName,
            Enabled = provider.Enabled,
            EvaluatedCases = evaluated.Count,
            ErrorCases = errorCount,
            IntentAccuracy = intentAccuracy,
            ParameterAccuracy = paramAccuracy,
            AverageConfidence = averageConfidence,
            TotalDurationMs = totalDurationMs,
            AverageDurationMs = averageDurationMs,
            RuntimeMeasuredCases = runtimeBearing.Count,
            TotalInputTokens = totalInputTokens,
            TotalOutputTokens = totalOutputTokens,
            TokenUsageCases = tokenBearing.Count,
            TokenEstimatedCases = outcomes.Count(outcome => outcome.TokensEstimated == true),
            TotalCostUsd = totalCostUsd,
            AverageCostUsd = averageCostUsd,
            CostMeasuredCases = costBearing.Count,
            CostSource = SummarizeSingleValue(outcomes.Select(outcome => outcome.CostSource)),
            ModelName = SummarizeSingleValue(outcomes.Select(outcome => outcome.ModelName)),
            PerIntent = perIntent,
            ConfusionMatrix = confusionMatrix,
            ConfidenceThresholds = ConfidenceThresholds.Select(threshold => BuildThresholdSummary(outcomes, threshold)).ToList(),
            ResponseTypes = responseTypes,
            DiagnosticKinds = diagnosticKinds,
            SelectedTools = selectedTools,
            FallbackCases = evaluated.Count(outcome => !string.IsNullOrWhiteSpace(outcome.FallbackReason))
        };
    }

    private static IntentBreakdownSummary BuildIntentBreakdown(List<EvalRow> rows, string providerKey)
    {
        var outcomes = rows.Select(row => GetOutcome(row, providerKey)).ToList();
        var evaluated = outcomes.Where(outcome => outcome.Error is null).ToList();
        var errorCount = outcomes.Count - evaluated.Count;

        double intentAccuracy = evaluated.Count == 0 ? 0 : evaluated.Count(outcome => outcome.IntentMatch) / (double)evaluated.Count;
        double paramAccuracy = evaluated.Count == 0 ? 0 : evaluated.Count(outcome => outcome.ParamMatch) / (double)evaluated.Count;

        return new IntentBreakdownSummary
        {
            TotalCases = outcomes.Count,
            EvaluatedCases = evaluated.Count,
            ErrorCases = errorCount,
            IntentAccuracy = intentAccuracy,
            ParameterAccuracy = paramAccuracy,
            PredictedIntentCounts = BuildCountDictionary(evaluated.Select(outcome => NormalizeIntentLabel(outcome.Intent)))
        };
    }

    private static ConfidenceThresholdSummary BuildThresholdSummary(List<ProviderOutcome> outcomes, double threshold)
    {
        var evaluated = outcomes.Where(outcome => outcome.Error is null).ToList();
        var confidenceBearing = evaluated.Where(outcome => outcome.Confidence.HasValue).ToList();
        var atOrAboveThreshold = confidenceBearing.Where(outcome => (outcome.Confidence ?? 0) >= threshold).ToList();

        int? casesAtOrAboveThreshold = confidenceBearing.Count == 0 ? null : atOrAboveThreshold.Count;
        int? casesBelowThreshold = confidenceBearing.Count == 0 ? null : confidenceBearing.Count - atOrAboveThreshold.Count;
        double? coverage = confidenceBearing.Count == 0 ? null : atOrAboveThreshold.Count / (double)confidenceBearing.Count;
        double? intentAccuracy = atOrAboveThreshold.Count == 0
            ? null
            : atOrAboveThreshold.Count(outcome => outcome.IntentMatch) / (double)atOrAboveThreshold.Count;
        double? paramAccuracy = atOrAboveThreshold.Count == 0
            ? null
            : atOrAboveThreshold.Count(outcome => outcome.ParamMatch) / (double)atOrAboveThreshold.Count;

        return new ConfidenceThresholdSummary
        {
            Threshold = threshold,
            CasesAtOrAboveThreshold = casesAtOrAboveThreshold,
            CasesBelowThreshold = casesBelowThreshold,
            Coverage = coverage,
            IntentAccuracyAtOrAboveThreshold = intentAccuracy,
            ParameterAccuracyAtOrAboveThreshold = paramAccuracy
        };
    }

    private static Dictionary<string, int> BuildCountDictionary(IEnumerable<string?> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
    }

    private static ProviderOutcome GetOutcome(EvalRow row, string providerKey)
    {
        if (row.Outcomes.TryGetValue(providerKey, out var outcome))
        {
            return outcome;
        }

        return new ProviderOutcome { Error = "Provider outcome missing" };
    }

    private static bool HasDiagnosticData(ProviderOutcome outcome)
    {
        return !string.IsNullOrWhiteSpace(outcome.ResponseType)
            || !string.IsNullOrWhiteSpace(outcome.ToolName)
            || !string.IsNullOrWhiteSpace(outcome.DiagnosticKind)
            || !string.IsNullOrWhiteSpace(outcome.FallbackReason)
            || !string.IsNullOrWhiteSpace(outcome.RawArgumentsJson)
            || !string.IsNullOrWhiteSpace(outcome.RawResponseJson);
    }

    private static string NormalizeIntentLabel(string? intent)
    {
        return string.IsNullOrWhiteSpace(intent) ? "<missing>" : intent;
    }

    private static string? SummarizeSingleValue(IEnumerable<string?> values)
    {
        var distinctValues = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return distinctValues.Count switch
        {
            0 => null,
            1 => distinctValues[0],
            _ => "mixed"
        };
    }

    private static List<string>? GetComparisonMetricLabels(BaselineComparisonMetrics metrics)
    {
        var labels = new List<string>();
        if (metrics.HasFlag(BaselineComparisonMetrics.Runtime))
        {
            labels.Add("runtime");
        }

        if (metrics.HasFlag(BaselineComparisonMetrics.Cost))
        {
            labels.Add("cost");
        }

        return labels.Count == 0 ? null : labels;
    }

    private static string FreezeBaselineArtifacts(
        string projectRoot,
        string datasetPath,
        string sourceRunDirectory,
        RunSummary summary,
        IReadOnlyList<EvalProvider> providers,
        string freezeBaselineSpec,
        string[] args)
    {
        if (providers.Count != 1)
        {
            throw new ArgumentException("--freeze-baseline requires exactly one selected provider.");
        }

        var provider = providers[0];
        if (!summary.Providers.TryGetValue(provider.Key, out var providerSummary))
        {
            throw new ArgumentException($"Summary does not contain provider '{provider.Key}'.");
        }

        var targetDirectory = ResolveBaselineOutputDirectory(projectRoot, freezeBaselineSpec);
        Directory.CreateDirectory(targetDirectory);

        CopyArtifact(sourceRunDirectory, targetDirectory, "summary.json");
        CopyArtifact(sourceRunDirectory, targetDirectory, "results.csv");
        CopyArtifact(sourceRunDirectory, targetDirectory, "needle_diagnostics.json");

        var manifest = new BaselineManifest
        {
            BaselineId = Path.GetFileName(targetDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            ProviderKey = provider.Key,
            ProviderDisplayName = provider.DisplayName,
            DatasetPath = HarnessPathUtils.ToProjectRelative(projectRoot, datasetPath),
            CreatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            SourceRunDirectory = HarnessPathUtils.ToProjectRelative(projectRoot, sourceRunDirectory),
            Command = BuildHarnessCommand(args),
            TotalCases = summary.TotalCases,
            IntentAccuracy = providerSummary.IntentAccuracy,
            ParameterAccuracy = providerSummary.ParameterAccuracy,
            AverageConfidence = providerSummary.AverageConfidence,
            AverageDurationMs = providerSummary.AverageDurationMs,
            TotalCostUsd = providerSummary.TotalCostUsd,
            Notes = BuildBaselineNotes(providerSummary)
        };

        File.WriteAllText(
            Path.Combine(targetDirectory, "manifest.json"),
            JsonSerializer.Serialize(manifest, PrettyJsonOptions));

        File.WriteAllText(
            Path.Combine(targetDirectory, "README.md"),
            BuildBaselineReadme(manifest, providerSummary));

        return targetDirectory;
    }

    private static string ResolveBaselineOutputDirectory(string projectRoot, string freezeBaselineSpec)
    {
        if (Path.IsPathRooted(freezeBaselineSpec))
        {
            return freezeBaselineSpec;
        }

        var normalizedSpec = freezeBaselineSpec.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (normalizedSpec.Contains(Path.DirectorySeparatorChar))
        {
            return Path.GetFullPath(Path.Combine(projectRoot, normalizedSpec));
        }

        return Path.GetFullPath(Path.Combine(projectRoot, "Baselines", normalizedSpec));
    }

    private static void CopyArtifact(string sourceDirectory, string targetDirectory, string fileName)
    {
        var sourcePath = Path.Combine(sourceDirectory, fileName);
        if (!File.Exists(sourcePath))
        {
            throw new ArgumentException($"Cannot freeze baseline because '{sourcePath}' does not exist.");
        }

        var targetPath = Path.Combine(targetDirectory, fileName);
        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    private static string BuildHarnessCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return "dotnet run --project IntentEvalHarness";
        }

        return $"dotnet run --project IntentEvalHarness -- {string.Join(" ", args.Select(QuoteCommandLineArgument))}";
    }

    private static string QuoteCommandLineArgument(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        return argument.Any(static ch => char.IsWhiteSpace(ch) || ch == '"')
            ? $"\"{argument.Replace("\"", "\\\"")}\""
            : argument;
    }

    private static List<string> BuildBaselineNotes(ProviderSummaryReport providerSummary)
    {
        var notes = new List<string>
        {
            "Frozen baseline generated by IntentEvalHarness --freeze-baseline."
        };

        if (providerSummary.AverageDurationMs.HasValue || providerSummary.TotalCostUsd.HasValue)
        {
            notes.Add("This baseline includes runtime and cost metrics for --compare-metrics reporting.");
        }

        if (providerSummary.CostSource == "free")
        {
            notes.Add("Provider cost is treated as free in the harness output.");
        }

        return notes;
    }

    private static string BuildBaselineReadme(BaselineManifest manifest, ProviderSummaryReport providerSummary)
    {
        var lines = new List<string>
        {
            $"# Frozen {manifest.ProviderDisplayName} Baseline",
            string.Empty,
            "This directory freezes one evaluation run for IntentEvalHarness.",
            string.Empty,
            $"Baseline id: `{manifest.BaselineId}`",
            $"Provider: `{manifest.ProviderDisplayName}` (`{manifest.ProviderKey}`)",
            $"Source run: `{manifest.SourceRunDirectory ?? "unknown"}`",
            $"Command: `{manifest.Command ?? "unknown"}`",
            $"Dataset: `{manifest.DatasetPath}`",
            string.Empty,
            "Summary metrics:",
            $"- Intent accuracy: `{FormatDouble(providerSummary.IntentAccuracy, 4)}`",
            $"- Parameter accuracy: `{FormatDouble(providerSummary.ParameterAccuracy, 4)}`",
            $"- Total cases: `{summaryTotalCases(providerSummary, manifest)}`"
        };

        if (providerSummary.AverageConfidence.HasValue)
        {
            lines.Add($"- Average confidence: `{FormatDouble(providerSummary.AverageConfidence.Value, 4)}`");
        }

        if (providerSummary.AverageDurationMs.HasValue)
        {
            lines.Add($"- Average duration ms: `{FormatDouble(providerSummary.AverageDurationMs.Value, 1)}`");
        }

        if (providerSummary.TotalCostUsd.HasValue)
        {
            lines.Add($"- Total cost usd: `{FormatDouble((double)providerSummary.TotalCostUsd.Value, 6)}`");
        }

        lines.Add(string.Empty);
        lines.Add("Files:");
        lines.Add("- `summary.json`: frozen aggregate metrics");
        lines.Add("- `results.csv`: one row per evaluated case");
        lines.Add("- `needle_diagnostics.json`: provider diagnostics copied from the source run (may be empty)");
        lines.Add("- `manifest.json`: metadata for the frozen baseline");

        if (manifest.Notes.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Notes:");
            lines.AddRange(manifest.Notes.Select(note => $"- {note}"));
        }

        return string.Join(Environment.NewLine, lines);

        static string summaryTotalCases(ProviderSummaryReport summary, BaselineManifest currentManifest)
        {
            return (currentManifest.TotalCases ?? summary.EvaluatedCases).ToString(CultureInfo.InvariantCulture);
        }
    }

    private static void PrintBaselineComparisonSummary(BaselineComparisonReport report, BaselineComparisonMetrics metrics)
    {
        Console.WriteLine();
        Console.WriteLine("=== Baseline Comparison ===");
        Console.WriteLine($"Baseline: {report.BaselineProviderDisplayName} ({report.BaselineProviderKey})");
        Console.WriteLine($"Cases: {report.CurrentTotalCases} current vs {report.BaselineTotalCases} baseline");
        Console.WriteLine("Cells show current value with delta in parentheses.");

        var columns = new List<(string Header, Func<ProviderComparisonReport, string> Value)>
        {
            ("Provider", comparison => $"{comparison.DisplayName} ({comparison.ProviderKey})"),
            ("Intent", comparison => FormatCompactMetric(comparison.IntentAccuracy, 4)),
            ("Params", comparison => FormatCompactMetric(comparison.ParameterAccuracy, 4)),
            ("Conf", comparison => FormatCompactMetric(comparison.AverageConfidence, 4))
        };

        if (metrics.HasFlag(BaselineComparisonMetrics.Runtime))
        {
            columns.Add(("Avg ms", comparison => FormatCompactMetric(comparison.AverageDurationMs, 1)));
        }

        if (metrics.HasFlag(BaselineComparisonMetrics.Cost))
        {
            columns.Add(("Avg $", comparison => FormatCompactMetric(comparison.AverageCostUsd, 6)));
            columns.Add(("Total $", comparison => FormatCompactMetric(comparison.TotalCostUsd, 6)));
        }

        var rows = report.Providers.Values
            .OrderBy(entry => entry.ProviderKey, StringComparer.OrdinalIgnoreCase)
            .Select(comparison => columns.Select(column => column.Value(comparison)).ToList())
            .ToList();

        var widths = columns
            .Select((column, index) => Math.Max(
                column.Header.Length,
                rows.Count == 0 ? 0 : rows.Max(row => row[index].Length)))
            .ToList();

        Console.WriteLine(string.Join(" | ", columns.Select((column, index) => column.Header.PadRight(widths[index]))));
        Console.WriteLine(string.Join("-+-", widths.Select(width => new string('-', width))));

        foreach (var row in rows)
        {
            Console.WriteLine(string.Join(" | ", row.Select((cell, index) => cell.PadRight(widths[index]))));
        }
    }

    private static string FormatCompactMetric(MetricDelta? metric, int decimals)
    {
        if (metric?.Current is null)
        {
            return "n/a";
        }

        if (metric.Baseline is null || metric.Delta is null)
        {
            return $"{FormatDouble(metric.Current.Value, decimals)} (n/a)";
        }

        return $"{FormatDouble(metric.Current.Value, decimals)} ({FormatSignedDouble(metric.Delta.Value, decimals)})";
    }

    private static string FormatDouble(double value, int decimals)
    {
        return value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }

    private static string FormatSignedDouble(double value, int decimals)
    {
        var formatted = FormatDouble(value, decimals);
        return value > 0 ? $"+{formatted}" : formatted;
    }

    private static void PrintSummary(RunSummary summary)
    {
        Console.WriteLine();
        Console.WriteLine("=== Intent Evaluation Summary ===");
        Console.WriteLine(JsonSerializer.Serialize(summary, PrettyJsonOptions));
    }
}
