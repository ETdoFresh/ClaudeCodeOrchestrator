using System.Text.Json;
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
                Content = new UserContent { Text = text }
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

    /// <summary>
    /// Content can be a string or an array of content blocks (tool results, etc.)
    /// </summary>
    [JsonPropertyName("content")]
    [JsonConverter(typeof(UserContentConverter))]
    public required UserContent Content { get; init; }
}

/// <summary>
/// User content wrapper that handles both string and array formats.
/// </summary>
public sealed record UserContent
{
    /// <summary>
    /// Text content (when content is a simple string).
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Content blocks (when content is an array, e.g., tool results).
    /// </summary>
    public IReadOnlyList<UserContentBlock>? Blocks { get; init; }

    /// <summary>
    /// Gets the text representation of the content.
    /// </summary>
    public string GetText()
    {
        if (Text != null) return Text;
        if (Blocks != null && Blocks.Count > 0)
        {
            return string.Join("\n", Blocks.Select(b => b.Content?.ToString() ?? b.Type));
        }
        return string.Empty;
    }
}

/// <summary>
/// A content block in user message (tool result, text, etc.)
/// </summary>
public sealed record UserContentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "text";

    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; init; }

    [JsonPropertyName("content")]
    public object? Content { get; init; }
}

/// <summary>
/// JSON converter for UserContent that handles both string and array formats.
/// </summary>
public class UserContentConverter : JsonConverter<UserContent>
{
    public override UserContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new UserContent { Text = reader.GetString() };
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var blocks = JsonSerializer.Deserialize<List<UserContentBlock>>(ref reader, options);
            return new UserContent { Blocks = blocks };
        }

        throw new JsonException($"Expected string or array for user content, got {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, UserContent value, JsonSerializerOptions options)
    {
        if (value.Text != null)
        {
            writer.WriteStringValue(value.Text);
        }
        else if (value.Blocks != null)
        {
            JsonSerializer.Serialize(writer, value.Blocks, options);
        }
        else
        {
            writer.WriteStringValue(string.Empty);
        }
    }
}
