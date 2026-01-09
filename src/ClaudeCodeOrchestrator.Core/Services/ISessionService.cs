using ClaudeCodeOrchestrator.Core.Models;
using ClaudeCodeOrchestrator.Git.Models;
using ClaudeCodeOrchestrator.SDK.Messages;
using ClaudeCodeOrchestrator.SDK.Options;

namespace ClaudeCodeOrchestrator.Core.Services;

/// <summary>
/// Represents image data for sending to Claude.
/// </summary>
public interface IImageData
{
    /// <summary>
    /// MIME type (e.g., "image/png", "image/jpeg").
    /// </summary>
    string MediaType { get; }

    /// <summary>
    /// Base64-encoded image data.
    /// </summary>
    string Base64Data { get; }
}

/// <summary>
/// Service for managing Claude Code sessions.
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Creates a new session in a worktree.
    /// </summary>
    Task<Session> CreateSessionAsync(
        WorktreeInfo worktree,
        string prompt,
        ClaudeAgentOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new session in a worktree with images.
    /// </summary>
    Task<Session> CreateSessionAsync(
        WorktreeInfo worktree,
        string prompt,
        IReadOnlyList<IImageData>? images,
        ClaudeAgentOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new idle session that waits for user input without sending a prompt.
    /// </summary>
    Task<Session> CreateIdleSessionAsync(
        WorktreeInfo worktree,
        ClaudeAgentOptions? options = null,
        IReadOnlyList<ISDKMessage>? historyMessages = null);

    /// <summary>
    /// Resumes an existing session.
    /// </summary>
    Task<Session> ResumeSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends a session.
    /// </summary>
    Task EndSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to a session.
    /// </summary>
    Task SendMessageAsync(
        string sessionId,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message with images to a session.
    /// </summary>
    Task SendMessageAsync(
        string sessionId,
        string message,
        IReadOnlyList<IImageData>? images,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Interrupts a session.
    /// </summary>
    Task InterruptSessionAsync(string sessionId);

    /// <summary>
    /// Gets a session by ID.
    /// </summary>
    Session? GetSession(string sessionId);

    /// <summary>
    /// Gets a session by worktree ID.
    /// </summary>
    Session? GetSessionByWorktreeId(string worktreeId);

    /// <summary>
    /// Gets all active sessions.
    /// </summary>
    IReadOnlyList<Session> GetActiveSessions();

    /// <summary>
    /// Raised when a new session is created.
    /// </summary>
    event EventHandler<SessionCreatedEventArgs>? SessionCreated;

    /// <summary>
    /// Raised when a message is received.
    /// </summary>
    event EventHandler<SessionMessageEventArgs>? MessageReceived;

    /// <summary>
    /// Raised when session state changes.
    /// </summary>
    event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged;

    /// <summary>
    /// Raised when a session ends.
    /// </summary>
    event EventHandler<SessionEndedEventArgs>? SessionEnded;

    /// <summary>
    /// Raised when a Claude session ID is received (from init message).
    /// </summary>
    event EventHandler<ClaudeSessionIdReceivedEventArgs>? ClaudeSessionIdReceived;
}

/// <summary>
/// Event args for session created.
/// </summary>
public sealed class SessionCreatedEventArgs : EventArgs
{
    public required Session Session { get; init; }
}

/// <summary>
/// Event args for message received.
/// </summary>
public sealed class SessionMessageEventArgs : EventArgs
{
    public required string SessionId { get; init; }
    public required ISDKMessage Message { get; init; }
}

/// <summary>
/// Event args for state changed.
/// </summary>
public sealed class SessionStateChangedEventArgs : EventArgs
{
    public required Session Session { get; init; }
    public required SessionState PreviousState { get; init; }
}

/// <summary>
/// Event args for session ended.
/// </summary>
public sealed class SessionEndedEventArgs : EventArgs
{
    public required string SessionId { get; init; }
    public required SessionState FinalState { get; init; }
    public required DateTime EndedAt { get; init; }
}

/// <summary>
/// Event args for Claude session ID received.
/// </summary>
public sealed class ClaudeSessionIdReceivedEventArgs : EventArgs
{
    public required string SessionId { get; init; }
    public required string WorktreeId { get; init; }
    public required string ClaudeSessionId { get; init; }
}
