using System.Text.Json.Serialization;

namespace ClaudeCodeOrchestrator.SDK.Messages;

/// <summary>
/// Base class for content blocks in messages.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextContentBlock), "text")]
[JsonDerivedType(typeof(ImageContentBlock), "image")]
[JsonDerivedType(typeof(ToolUseContentBlock), "tool_use")]
[JsonDerivedType(typeof(ToolResultContentBlock), "tool_result")]
[JsonDerivedType(typeof(ThinkingContentBlock), "thinking")]
public abstract record ContentBlock
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }
}

/// <summary>
/// Text content block.
/// </summary>
public sealed record TextContentBlock : ContentBlock
{
    [JsonPropertyName("type")]
    public override string Type => "text";

    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

/// <summary>
/// Image content block for sending images to Claude.
/// </summary>
public sealed record ImageContentBlock : ContentBlock
{
    [JsonPropertyName("type")]
    public override string Type => "image";

    [JsonPropertyName("source")]
    public required ImageSource Source { get; init; }

    /// <summary>
    /// Creates an image content block from base64 data.
    /// </summary>
    public static ImageContentBlock FromBase64(string base64Data, string mediaType)
    {
        return new ImageContentBlock
        {
            Source = new ImageSource
            {
                Type = "base64",
                MediaType = mediaType,
                Data = base64Data
            }
        };
    }
}

/// <summary>
/// Image source for image content blocks.
/// </summary>
public sealed record ImageSource
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("media_type")]
    public required string MediaType { get; init; }

    [JsonPropertyName("data")]
    public required string Data { get; init; }
}

/// <summary>
/// Tool use content block representing Claude's request to use a tool.
/// </summary>
public sealed record ToolUseContentBlock : ContentBlock
{
    [JsonPropertyName("type")]
    public override string Type => "tool_use";

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("input")]
    public required object Input { get; init; }
}

/// <summary>
/// Tool result content block containing the result of a tool execution.
/// </summary>
public sealed record ToolResultContentBlock : ContentBlock
{
    [JsonPropertyName("type")]
    public override string Type => "tool_result";

    [JsonPropertyName("tool_use_id")]
    public required string ToolUseId { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("is_error")]
    public bool IsError { get; init; }
}

/// <summary>
/// Thinking content block for extended thinking output.
/// </summary>
public sealed record ThinkingContentBlock : ContentBlock
{
    [JsonPropertyName("type")]
    public override string Type => "thinking";

    [JsonPropertyName("thinking")]
    public required string Thinking { get; init; }
}
