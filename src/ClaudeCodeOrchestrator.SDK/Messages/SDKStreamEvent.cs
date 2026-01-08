using System.Text.Json.Serialization;

namespace ClaudeCodeOrchestrator.SDK.Messages;

/// <summary>
/// Partial streaming event for real-time updates.
/// </summary>
public sealed record SDKStreamEvent : ISDKMessage
{
    [JsonPropertyName("type")]
    public string Type => "stream_event";

    [JsonPropertyName("uuid")]
    public required string Uuid { get; init; }

    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("event")]
    public required StreamEvent Event { get; init; }

    [JsonPropertyName("parent_tool_use_id")]
    public string? ParentToolUseId { get; init; }
}

/// <summary>
/// A streaming event with delta updates.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ContentBlockStartEvent), "content_block_start")]
[JsonDerivedType(typeof(ContentBlockDeltaEvent), "content_block_delta")]
[JsonDerivedType(typeof(ContentBlockStopEvent), "content_block_stop")]
[JsonDerivedType(typeof(MessageStartEvent), "message_start")]
[JsonDerivedType(typeof(MessageDeltaEvent), "message_delta")]
[JsonDerivedType(typeof(MessageStopEvent), "message_stop")]
public abstract record StreamEvent
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }
}

public sealed record ContentBlockStartEvent : StreamEvent
{
    [JsonPropertyName("type")]
    public override string Type => "content_block_start";

    [JsonPropertyName("index")]
    public required int Index { get; init; }

    [JsonPropertyName("content_block")]
    public required ContentBlock ContentBlock { get; init; }
}

public sealed record ContentBlockDeltaEvent : StreamEvent
{
    [JsonPropertyName("type")]
    public override string Type => "content_block_delta";

    [JsonPropertyName("index")]
    public required int Index { get; init; }

    [JsonPropertyName("delta")]
    public required ContentDelta Delta { get; init; }
}

public sealed record ContentBlockStopEvent : StreamEvent
{
    [JsonPropertyName("type")]
    public override string Type => "content_block_stop";

    [JsonPropertyName("index")]
    public required int Index { get; init; }
}

public sealed record MessageStartEvent : StreamEvent
{
    [JsonPropertyName("type")]
    public override string Type => "message_start";

    [JsonPropertyName("message")]
    public required object Message { get; init; }
}

public sealed record MessageDeltaEvent : StreamEvent
{
    [JsonPropertyName("type")]
    public override string Type => "message_delta";

    [JsonPropertyName("delta")]
    public required MessageDelta Delta { get; init; }

    [JsonPropertyName("usage")]
    public TokenUsage? Usage { get; init; }
}

public sealed record MessageStopEvent : StreamEvent
{
    [JsonPropertyName("type")]
    public override string Type => "message_stop";
}

/// <summary>
/// Delta update for content blocks.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextDelta), "text_delta")]
[JsonDerivedType(typeof(InputJsonDelta), "input_json_delta")]
[JsonDerivedType(typeof(ThinkingDelta), "thinking_delta")]
public abstract record ContentDelta
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }
}

public sealed record TextDelta : ContentDelta
{
    [JsonPropertyName("type")]
    public override string Type => "text_delta";

    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

public sealed record InputJsonDelta : ContentDelta
{
    [JsonPropertyName("type")]
    public override string Type => "input_json_delta";

    [JsonPropertyName("partial_json")]
    public required string PartialJson { get; init; }
}

public sealed record ThinkingDelta : ContentDelta
{
    [JsonPropertyName("type")]
    public override string Type => "thinking_delta";

    [JsonPropertyName("thinking")]
    public required string Thinking { get; init; }
}

/// <summary>
/// Delta update for message metadata.
/// </summary>
public sealed record MessageDelta
{
    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; init; }

    [JsonPropertyName("stop_sequence")]
    public string? StopSequence { get; init; }
}
