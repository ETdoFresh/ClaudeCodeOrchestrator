using System.Text.Json.Serialization;

namespace ClaudeCodeOrchestrator.SDK.Messages;

/// <summary>
/// Message from the user.
/// </summary>
public sealed record SDKUserMessage : ISDKMessage
{
    [JsonPropertyName("type")]
    public string Type => "user";

    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("message")]
    public required UserMessageContent Message { get; init; }

    [JsonPropertyName("parent_tool_use_id")]
    public string? ParentToolUseId { get; init; }

    /// <summary>
    /// Creates a simple text user message for sending to Claude.
    /// </summary>
    public static SDKUserMessage CreateText(string text, string sessionId = "")
    {
        return new SDKUserMessage
        {
            SessionId = sessionId,
            Message = new UserMessageContent
            {
                Content = text
            }
        };
    }
}

/// <summary>
/// Content of a user message.
/// </summary>
public sealed record UserMessageContent
{
    [JsonPropertyName("role")]
    public string Role => "user";

    [JsonPropertyName("content")]
    public required string Content { get; init; }
}
