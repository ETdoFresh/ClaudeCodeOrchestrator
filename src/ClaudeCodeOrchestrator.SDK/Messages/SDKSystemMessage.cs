using System.Text.Json.Serialization;

namespace ClaudeCodeOrchestrator.SDK.Messages;

/// <summary>
/// System message from Claude Code (init, compact_boundary, etc.).
/// </summary>
public sealed record SDKSystemMessage : ISDKMessage
{
    [JsonPropertyName("type")]
    public string Type => "system";

    [JsonPropertyName("subtype")]
    public required string Subtype { get; init; }

    [JsonPropertyName("uuid")]
    public required string Uuid { get; init; }

    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    // Init-specific properties
    [JsonPropertyName("api_key_source")]
    public string? ApiKeySource { get; init; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }

    [JsonPropertyName("tools")]
    public IReadOnlyList<string>? Tools { get; init; }

    [JsonPropertyName("mcp_servers")]
    public IReadOnlyList<McpServerInfo>? McpServers { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("permission_mode")]
    public string? PermissionMode { get; init; }

    [JsonPropertyName("slash_commands")]
    public IReadOnlyList<string>? SlashCommands { get; init; }

    // Compact boundary specific
    [JsonPropertyName("compact_metadata")]
    public CompactMetadata? CompactMetadata { get; init; }
}

/// <summary>
/// Information about an MCP server.
/// </summary>
public sealed record McpServerInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("tools")]
    public IReadOnlyList<string>? Tools { get; init; }
}

/// <summary>
/// Metadata about conversation compaction.
/// </summary>
public sealed record CompactMetadata
{
    [JsonPropertyName("messages_removed")]
    public int MessagesRemoved { get; init; }

    [JsonPropertyName("tokens_saved")]
    public int TokensSaved { get; init; }
}

/// <summary>
/// System message subtypes.
/// </summary>
public static class SystemSubtype
{
    public const string Init = "init";
    public const string CompactBoundary = "compact_boundary";
}
