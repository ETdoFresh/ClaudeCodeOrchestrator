using System.Text.Json.Serialization;

namespace ClaudeCodeOrchestrator.SDK.Messages;

/// <summary>
/// Result message indicating the end of a query.
/// </summary>
public sealed record SDKResultMessage : ISDKMessage
{
    [JsonPropertyName("type")]
    public string Type => "result";

    [JsonPropertyName("subtype")]
    public string Subtype { get; init; } = "success";

    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; init; }

    [JsonPropertyName("duration_api_ms")]
    public long DurationApiMs { get; init; }

    [JsonPropertyName("is_error")]
    public bool IsError { get; init; }

    [JsonPropertyName("num_turns")]
    public int NumTurns { get; init; }

    [JsonPropertyName("result")]
    public string? Result { get; init; }

    [JsonPropertyName("errors")]
    public IReadOnlyList<string>? Errors { get; init; }

    [JsonPropertyName("total_cost_usd")]
    public decimal TotalCostUsd { get; init; }

    [JsonPropertyName("usage")]
    public TokenUsage? Usage { get; init; }

    [JsonPropertyName("modelUsage")]
    public IReadOnlyDictionary<string, ModelUsage>? ModelUsage { get; init; }

    [JsonPropertyName("permission_denials")]
    public IReadOnlyList<PermissionDenial>? PermissionDenials { get; init; }

    [JsonPropertyName("structured_output")]
    public object? StructuredOutput { get; init; }
}

/// <summary>
/// Usage information for a specific model.
/// </summary>
public sealed record ModelUsage
{
    [JsonPropertyName("inputTokens")]
    public int InputTokens { get; init; }

    [JsonPropertyName("outputTokens")]
    public int OutputTokens { get; init; }

    [JsonPropertyName("cacheCreationInputTokens")]
    public int? CacheCreationInputTokens { get; init; }

    [JsonPropertyName("cacheReadInputTokens")]
    public int? CacheReadInputTokens { get; init; }

    [JsonPropertyName("costUSD")]
    public decimal? CostUsd { get; init; }

    [JsonPropertyName("contextWindow")]
    public int? ContextWindow { get; init; }

    [JsonPropertyName("webSearchRequests")]
    public int? WebSearchRequests { get; init; }
}

/// <summary>
/// Information about a permission denial.
/// </summary>
public sealed record PermissionDenial
{
    [JsonPropertyName("tool")]
    public string? Tool { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("tool_name")]
    public string? ToolName { get; init; }

    [JsonPropertyName("file_path")]
    public string? FilePath { get; init; }
}

/// <summary>
/// Result subtypes for SDKResultMessage.
/// </summary>
public static class ResultSubtype
{
    public const string Success = "success";
    public const string ErrorMaxTurns = "error_max_turns";
    public const string ErrorDuringExecution = "error_during_execution";
    public const string ErrorInterrupt = "error_interrupt";
    public const string ErrorMaxBudget = "error_max_budget";
    public const string ErrorRateLimit = "error_rate_limit";
    public const string ErrorStop = "error_stop";
}
