namespace IntentEvalHarness;

public sealed class ServiceResponse<T>
{
    public T? Data { get; set; }
    public bool Success { get; set; } = true;
    public string Message { get; set; } = string.Empty;
}

public sealed class IntentEvaluationResult
{
    public string Intent { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Reasoning { get; set; } = string.Empty;
    public List<string> SuggestedActions { get; set; } = [];
}

public sealed class IntentEvaluationExecutionResult
{
    public IntentEvaluationResult Evaluation { get; set; } = new();
    public string ProviderName { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public bool TokensEstimated { get; set; }
    public int DurationMs { get; set; }
    public bool CacheHit { get; set; }
    public decimal CostUsd { get; set; }
}
