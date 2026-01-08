using ClaudeCodeOrchestrator.SDK.Messages;

namespace ClaudeCodeOrchestrator.Core.Models;

/// <summary>
/// Represents a Claude Code session.
/// </summary>
public sealed class Session
{
    /// <summary>
    /// Internal unique identifier for this session.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Session ID from Claude Code (from init message).
    /// </summary>
    public string? ClaudeSessionId { get; set; }

    /// <summary>
    /// ID of the worktree this session is associated with.
    /// </summary>
    public required string WorktreeId { get; init; }

    /// <summary>
    /// Current state of the session.
    /// </summary>
    public SessionState State { get; set; } = SessionState.Starting;

    /// <summary>
    /// When the session was created.
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// When the session ended (if applicable).
    /// </summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// Total cost in USD for this session.
    /// </summary>
    public decimal TotalCostUsd { get; set; }

    /// <summary>
    /// Messages in this session.
    /// </summary>
    public List<ISDKMessage> Messages { get; } = new();

    /// <summary>
    /// The initial prompt that started this session (null for resumed sessions).
    /// </summary>
    public string? InitialPrompt { get; init; }

    /// <summary>
    /// Title for display (derived from initial prompt or default).
    /// </summary>
    public string Title => string.IsNullOrEmpty(InitialPrompt)
        ? "Session"
        : (InitialPrompt.Length > 50 ? InitialPrompt[..47] + "..." : InitialPrompt);
}

/// <summary>
/// State of a session.
/// </summary>
public enum SessionState
{
    /// <summary>
    /// Session is starting up.
    /// </summary>
    Starting,

    /// <summary>
    /// Session is active and ready for input.
    /// </summary>
    Active,

    /// <summary>
    /// Session is waiting for user input (e.g., permission prompt).
    /// </summary>
    WaitingForInput,

    /// <summary>
    /// Session is processing a request.
    /// </summary>
    Processing,

    /// <summary>
    /// Session is paused.
    /// </summary>
    Paused,

    /// <summary>
    /// Session completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Session ended with an error.
    /// </summary>
    Error,

    /// <summary>
    /// Session was cancelled.
    /// </summary>
    Cancelled
}
