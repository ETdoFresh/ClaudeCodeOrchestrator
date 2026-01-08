using System.Runtime.CompilerServices;
using ClaudeCodeOrchestrator.SDK.Messages;
using ClaudeCodeOrchestrator.SDK.Options;
using ClaudeCodeOrchestrator.SDK.Streaming;

namespace ClaudeCodeOrchestrator.SDK;

/// <summary>
/// Represents an active Claude Code query with control methods.
/// Implements IAsyncEnumerable for streaming messages.
/// </summary>
public sealed class Query : IAsyncEnumerable<ISDKMessage>, IAsyncDisposable
{
    private readonly ProcessStreamHandler _handler;
    private readonly CancellationTokenSource _cts;
    private string? _sessionId;
    private bool _disposed;
    private bool _hasStartedEnumeration;

    internal Query(ProcessStreamHandler handler)
    {
        _handler = handler;
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// The session ID from Claude Code (available after init message).
    /// </summary>
    public string? SessionId => _sessionId;

    /// <summary>
    /// Whether the query is still active.
    /// </summary>
    public bool IsActive => !_disposed && !_cts.IsCancellationRequested;

    /// <summary>
    /// Gets an async enumerator for messages from Claude Code.
    /// </summary>
    public async IAsyncEnumerator<ISDKMessage> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        if (_hasStartedEnumeration)
        {
            throw new InvalidOperationException("Query can only be enumerated once.");
        }
        _hasStartedEnumeration = true;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

        await foreach (var message in _handler.ReadMessagesAsync(linkedCts.Token))
        {
            // Capture session ID from init message
            if (message is SDKSystemMessage { Subtype: SystemSubtype.Init } sysMsg)
            {
                _sessionId = sysMsg.SessionId;
            }

            yield return message;
        }
    }

    /// <summary>
    /// Sends a follow-up message to Claude Code (for streaming input mode).
    /// </summary>
    public async Task SendMessageAsync(string text, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var message = SDKUserMessage.CreateText(text, _sessionId ?? "");
        await _handler.SendMessageAsync(message, cancellationToken);
    }

    /// <summary>
    /// Sends an interrupt signal to stop the current operation.
    /// </summary>
    public async Task InterruptAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _handler.SendInterruptAsync(cancellationToken);
    }

    /// <summary>
    /// Requests to rewind files to a specific message.
    /// </summary>
    public async Task RewindFilesAsync(string userMessageUuid, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _handler.SendCommandAsync("rewind_files", new { user_message_uuid = userMessageUuid }, cancellationToken);
    }

    /// <summary>
    /// Changes the permission mode during a session.
    /// </summary>
    public async Task SetPermissionModeAsync(PermissionMode mode, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var modeString = mode switch
        {
            PermissionMode.AcceptAll => "acceptEdits",
            PermissionMode.Plan => "plan",
            _ => "default"
        };
        await _handler.SendCommandAsync("set_permission_mode", new { mode = modeString }, cancellationToken);
    }

    /// <summary>
    /// Changes the model during a session.
    /// </summary>
    public async Task SetModelAsync(string? model = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _handler.SendCommandAsync("set_model", new { model }, cancellationToken);
    }

    /// <summary>
    /// Sets the maximum thinking tokens for extended thinking.
    /// </summary>
    public async Task SetMaxThinkingTokensAsync(int? maxThinkingTokens, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _handler.SendCommandAsync("set_max_thinking_tokens", new { max_thinking_tokens = maxThinkingTokens }, cancellationToken);
    }

    /// <summary>
    /// Closes input to signal the end of the conversation.
    /// </summary>
    public void CloseInput()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _handler.CloseInput();
    }

    /// <summary>
    /// Cancels the query and stops reading messages.
    /// </summary>
    public void Cancel()
    {
        if (!_disposed)
        {
            _cts.Cancel();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _cts.CancelAsync();
        _cts.Dispose();
        await _handler.DisposeAsync();
    }
}
