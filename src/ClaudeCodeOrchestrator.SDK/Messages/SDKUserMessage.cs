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

    /// <summary>
    /// Creates a user message with text and optional images.
    /// </summary>
    public static SDKUserMessage CreateWithImages(string text, IReadOnlyList<ImageContentBlock> images, string sessionId = "")
    {
        if (images.Count == 0)
        {
            return CreateText(text, sessionId);
        }

        // Build content blocks: images first, then text
        var blocks = new List<UserContentBlock>();

        foreach (var image in images)
        {
            blocks.Add(new UserContentBlock
            {
                Type = "image",
                Source = new ImageBlockSource
                {
                    Type = "base64",
                    MediaType = image.Source.MediaType,
                    Data = image.Source.Data
                }
            });
        }

        blocks.Add(new UserContentBlock
        {
            Type = "text",
            Text = text
        });

        return new SDKUserMessage
        {
            SessionId = sessionId,
            Message = new UserMessageContent
            {
                Content = new UserContent { Blocks = blocks }
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
/// A content block in user message (tool result, text, image, etc.)
/// </summary>
[JsonConverter(typeof(UserContentBlockConverter))]
public sealed record UserContentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "text";

    [JsonPropertyName("tool_use_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolUseId { get; init; }

    /// <summary>
    /// Content for text and tool_result blocks.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Content { get; init; }

    /// <summary>
    /// Source for image blocks.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImageBlockSource? Source { get; init; }

    /// <summary>
    /// Text content for text blocks.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }
}

/// <summary>
/// Source for an image content block in user messages.
/// </summary>
public sealed record ImageBlockSource
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "base64";

    [JsonPropertyName("media_type")]
    public required string MediaType { get; init; }

    [JsonPropertyName("data")]
    public required string Data { get; init; }
}

/// <summary>
/// JSON converter for UserContentBlock that serializes based on block type.
/// </summary>
public class UserContentBlockConverter : JsonConverter<UserContentBlock>
{
    public override UserContentBlock Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var type = root.GetProperty("type").GetString() ?? "text";
        var block = new UserContentBlock { Type = type };

        if (type == "image" && root.TryGetProperty("source", out var sourceElement))
        {
            return block with
            {
                Source = new ImageBlockSource
                {
                    Type = sourceElement.GetProperty("type").GetString() ?? "base64",
                    MediaType = sourceElement.GetProperty("media_type").GetString() ?? "",
                    Data = sourceElement.GetProperty("data").GetString() ?? ""
                }
            };
        }
        else if (type == "text")
        {
            if (root.TryGetProperty("text", out var textElement))
            {
                return block with { Text = textElement.GetString() };
            }
            if (root.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
            {
                return block with { Text = contentElement.GetString() };
            }
        }
        else if (root.TryGetProperty("tool_use_id", out var toolUseIdElement))
        {
            var content = root.TryGetProperty("content", out var contentEl) ? contentEl.GetString() : null;
            return block with { ToolUseId = toolUseIdElement.GetString(), Content = content };
        }

        return block;
    }

    public override void Write(Utf8JsonWriter writer, UserContentBlock value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteString("type", value.Type);

        if (value.Type == "image" && value.Source != null)
        {
            writer.WritePropertyName("source");
            writer.WriteStartObject();
            writer.WriteString("type", value.Source.Type);
            writer.WriteString("media_type", value.Source.MediaType);
            writer.WriteString("data", value.Source.Data);
            writer.WriteEndObject();
        }
        else if (value.Type == "text")
        {
            writer.WriteString("text", value.Text ?? value.Content?.ToString() ?? "");
        }
        else if (value.Type == "tool_result")
        {
            if (value.ToolUseId != null)
            {
                writer.WriteString("tool_use_id", value.ToolUseId);
            }
            if (value.Content != null)
            {
                writer.WriteString("content", value.Content.ToString());
            }
        }

        writer.WriteEndObject();
    }
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
