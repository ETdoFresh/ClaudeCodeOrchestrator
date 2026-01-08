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
    public required string Subtype { get; init; }

    [JsonPropertyName("uuid")]
    public required string Uuid { get; init; }

    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("duration_ms")]
    public required long DurationMs { get; init; }

    [JsonPropertyName("duration_api_ms")]
    public required long DurationApiMs { get; init; }

    [JsonPropertyName("is_error")]
    public required bool IsError { get; init; }

    [JsonPropertyName("num_turns")]
    public required int NumTurns { get; init; }

    [JsonPropertyName("result")]
    public string? Result { get; init; }

    [JsonPropertyName("errors")]
    public IReadOnlyList<string>? Errors { get; init; }

    [JsonPropertyName("total_cost_usd")]
    public required decimal TotalCostUsd { get; init; }

    [JsonPropertyName("usage")]
    public required TokenUsage Usage { get; init; }

    [JsonPropertyName("model_usage")]
    public required IReadOnlyDictionary<string, ModelUsage> ModelUsage { get; init; }

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
    [JsonPropertyName("input_tokens")]
    public required int InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public required int OutputTokens { get; init; }

    [JsonPropertyName("cache_creation_input_tokens")]
    public int? CacheCreationInputTokens { get; init; }

    [JsonPropertyName("cache_read_input_tokens")]
    public int? CacheReadInputTokens { get; init; }

    [JsonPropertyName("cost_usd")]
    public decimal? CostUsd { get; init; }
}

/// <summary>
/// Information about a permission denial.
/// </summary>
public sealed record PermissionDenial
{
    [JsonPropertyName("tool")]
    public required string Tool { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
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
