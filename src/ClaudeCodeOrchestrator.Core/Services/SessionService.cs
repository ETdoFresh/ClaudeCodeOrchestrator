using System.Collections.Concurrent;
using ClaudeCodeOrchestrator.Core.Models;
using ClaudeCodeOrchestrator.Git.Models;
using ClaudeCodeOrchestrator.SDK;
using ClaudeCodeOrchestrator.SDK.Messages;
using ClaudeCodeOrchestrator.SDK.Options;

namespace ClaudeCodeOrchestrator.Core.Services;

/// <summary>
/// Implementation of session service.
/// </summary>
public sealed class SessionService : ISessionService, IDisposable
{
    private readonly ConcurrentDictionary<string, SessionContext> _sessions = new();
    private bool _disposed;

    public event EventHandler<SessionCreatedEventArgs>? SessionCreated;
    public event EventHandler<SessionMessageEventArgs>? MessageReceived;
    public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged;
    public event EventHandler<SessionEndedEventArgs>? SessionEnded;

    public async Task<Session> CreateSessionAsync(
        WorktreeInfo worktree,
        string prompt,
        ClaudeAgentOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ClaudeAgentOptions();
        options = options with
        {
            Cwd = worktree.Path
        };

        var session = new Session
        {
            Id = Guid.NewGuid().ToString(),
            WorktreeId = worktree.Id,
            CreatedAt = DateTime.UtcNow,
            InitialPrompt = prompt
        };

        var query = ClaudeAgent.CreateQuery(prompt, options);
        var cts = new CancellationTokenSource();
        var context = new SessionContext(session, query, cts);

        _sessions[session.Id] = context;

        SessionCreated?.Invoke(this, new SessionCreatedEventArgs { Session = session });

        // Start message processing in background
        _ = ProcessMessagesAsync(context, cancellationToken);

        return session;
    }

    public Task<Session> CreateIdleSessionAsync(
        WorktreeInfo worktree,
        ClaudeAgentOptions? options = null,
        IReadOnlyList<ISDKMessage>? historyMessages = null)
    {
        options ??= new ClaudeAgentOptions();
        options = options with
        {
            Cwd = worktree.Path
        };

        var session = new Session
        {
            Id = Guid.NewGuid().ToString(),
            WorktreeId = worktree.Id,
            CreatedAt = DateTime.UtcNow,
            State = SessionState.WaitingForInput
        };

        // Add history messages before firing the event
        if (historyMessages != null)
        {
            foreach (var msg in historyMessages)
            {
                session.Messages.Add(msg);
            }
        }

        // Create a streaming query that will be used when the user sends a message
        var query = ClaudeAgent.CreateStreamingQuery(options);
        var cts = new CancellationTokenSource();
        var context = new SessionContext(session, query, cts);

        _sessions[session.Id] = context;

        SessionCreated?.Invoke(this, new SessionCreatedEventArgs { Session = session });

        return Task.FromResult(session);
    }

    public async Task<Session> ResumeSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var context))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        if (string.IsNullOrEmpty(context.Session.ClaudeSessionId))
        {
            throw new InvalidOperationException("Session has no Claude session ID to resume");
        }

        var options = new ClaudeAgentOptions
        {
            Resume = context.Session.ClaudeSessionId
        };

        var query = ClaudeAgent.CreateStreamingQuery(options);
        var cts = new CancellationTokenSource();
        var newContext = new SessionContext(context.Session, query, cts);

        _sessions[sessionId] = newContext;

        var previousState = context.Session.State;
        context.Session.State = SessionState.Active;

        SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs
        {
            Session = context.Session,
            PreviousState = previousState
        });

        // Start message processing
        _ = ProcessMessagesAsync(newContext, cancellationToken);

        return context.Session;
    }

    public async Task EndSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var context))
        {
            await context.CancellationTokenSource.CancelAsync();
            await context.Query.DisposeAsync();
            context.Session.EndedAt = DateTime.UtcNow;

            SessionEnded?.Invoke(this, new SessionEndedEventArgs
            {
                SessionId = sessionId,
                FinalState = context.Session.State
            });
        }
    }

    public async Task SendMessageAsync(
        string sessionId,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var context))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        var previousState = context.Session.State;
        context.Session.State = SessionState.Processing;

        SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs
        {
            Session = context.Session,
            PreviousState = previousState
        });

        await context.Query.SendMessageAsync(message, cancellationToken);
    }

    public async Task InterruptSessionAsync(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var context))
        {
            await context.Query.InterruptAsync();
        }
    }

    public Session? GetSession(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var context) ? context.Session : null;
    }

    public IReadOnlyList<Session> GetActiveSessions()
    {
        return _sessions.Values
            .Where(c => c.Session.State is SessionState.Active or SessionState.Processing or SessionState.WaitingForInput)
            .Select(c => c.Session)
            .ToList();
    }

    private async Task ProcessMessagesAsync(SessionContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, context.CancellationTokenSource.Token);

            await foreach (var message in context.Query.WithCancellation(linkedCts.Token))
            {
                context.Session.Messages.Add(message);

                // Handle init message
                if (message is SDKSystemMessage { Subtype: SystemSubtype.Init } initMsg)
                {
                    context.Session.ClaudeSessionId = initMsg.SessionId;
                    var previousState = context.Session.State;
                    context.Session.State = SessionState.Active;

                    SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs
                    {
                        Session = context.Session,
                        PreviousState = previousState
                    });
                }

                // Handle result message
                if (message is SDKResultMessage resultMsg)
                {
                    var previousState = context.Session.State;
                    context.Session.State = resultMsg.IsError ? SessionState.Error : SessionState.Completed;
                    context.Session.TotalCostUsd = resultMsg.TotalCostUsd;
                    context.Session.EndedAt = DateTime.UtcNow;

                    SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs
                    {
                        Session = context.Session,
                        PreviousState = previousState
                    });
                }

                MessageReceived?.Invoke(this, new SessionMessageEventArgs
                {
                    SessionId = context.Session.Id,
                    Message = message
                });
            }
        }
        catch (OperationCanceledException)
        {
            context.Session.State = SessionState.Cancelled;
        }
        catch (Exception)
        {
            context.Session.State = SessionState.Error;
        }
        finally
        {
            context.Session.EndedAt ??= DateTime.UtcNow;

            SessionEnded?.Invoke(this, new SessionEndedEventArgs
            {
                SessionId = context.Session.Id,
                FinalState = context.Session.State
            });
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var context in _sessions.Values)
        {
            context.CancellationTokenSource.Cancel();
            context.Query.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
            context.CancellationTokenSource.Dispose();
        }

        _sessions.Clear();
    }

    private sealed record SessionContext(
        Session Session,
        Query Query,
        CancellationTokenSource CancellationTokenSource);
}
