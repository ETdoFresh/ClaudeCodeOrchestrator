namespace ClaudeCodeOrchestrator.SDK.Messages;

/// <summary>
/// Base interface for all SDK messages from Claude Code.
/// </summary>
public interface ISDKMessage
{
    /// <summary>
    /// The type of message (e.g., "assistant", "user", "result", "system").
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Unique identifier for this message.
    /// </summary>
    string Uuid { get; }

    /// <summary>
    /// The session ID this message belongs to.
    /// </summary>
    string SessionId { get; }
}
