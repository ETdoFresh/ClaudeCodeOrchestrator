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

    /// <summary>
    /// Additional system prompt instructions appended to all sessions.
    /// </summary>
    private const string AdditionalSystemPrompt = """
        IMPORTANT: After completing any code changes, you MUST commit your changes using git.
        Create a clear, descriptive commit message summarizing what was changed.
        Do not leave uncommitted changes in the worktree.
        """;

    public event EventHandler<SessionCreatedEventArgs>? SessionCreated;
    public event EventHandler<SessionMessageEventArgs>? MessageReceived;
    public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged;
    public event EventHandler<SessionEndedEventArgs>? SessionEnded;
    public event EventHandler<ClaudeSessionIdReceivedEventArgs>? ClaudeSessionIdReceived;

    public async Task<Session> CreateSessionAsync(
        WorktreeInfo worktree,
        string prompt,
        ClaudeAgentOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ClaudeAgentOptions();
        options = options with
        {
            Cwd = worktree.Path,
            PermissionMode = PermissionMode.AcceptAll,
            SystemPrompt = new SystemPromptConfig
            {
                Append = AdditionalSystemPrompt
            }
        };

        var session = new Session
        {
            Id = Guid.NewGuid().ToString(),
            WorktreeId = worktree.Id,
            WorktreePath = worktree.Path,
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
            Cwd = worktree.Path,
            PermissionMode = PermissionMode.AcceptAll,
            SystemPrompt = new SystemPromptConfig
            {
                Append = AdditionalSystemPrompt
            }
        };

        var session = new Session
        {
            Id = Guid.NewGuid().ToString(),
            WorktreeId = worktree.Id,
            WorktreePath = worktree.Path,
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
            Resume = context.Session.ClaudeSessionId,
            PermissionMode = PermissionMode.AcceptAll
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

        // If session has completed, we need to resume it first
        if (previousState is SessionState.Completed or SessionState.Error or SessionState.Cancelled)
        {
            if (string.IsNullOrEmpty(context.Session.ClaudeSessionId))
            {
                throw new InvalidOperationException("Session has no Claude session ID to resume");
            }

            // Dispose the old query
            await context.Query.DisposeAsync();

            // Create a new query that resumes the session with the message as the prompt
            // Using ClaudeAgent.ResumeSession which passes the prompt via -p flag
            var options = new ClaudeAgentOptions
            {
                Cwd = context.Session.WorktreePath,
                PermissionMode = PermissionMode.AcceptAll,
                SystemPrompt = new SystemPromptConfig
                {
                    Append = AdditionalSystemPrompt
                }
            };

            var query = ClaudeAgent.ResumeSession(context.Session.ClaudeSessionId, message, options);
            var cts = new CancellationTokenSource();
            var newContext = new SessionContext(context.Session, query, cts);
            _sessions[sessionId] = newContext;
            context = newContext;

            context.Session.State = SessionState.Processing;

            SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs
            {
                Session = context.Session,
                PreviousState = previousState
            });

            // Start message processing for the resumed session
            _ = ProcessMessagesAsync(context, cancellationToken);

            // Message was already sent via the prompt, no need to call SendMessageAsync
            return;
        }

        // Start message processing if this is the first message (idle session)
        if (previousState == SessionState.WaitingForInput)
        {
            context.Session.State = SessionState.Processing;

            SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs
            {
                Session = context.Session,
                PreviousState = previousState
            });

            _ = ProcessMessagesAsync(context, cancellationToken);
        }
        // If session is Active or Processing, just send the message - it will be injected at next tool boundary

        await context.Query.SendMessageAsync(message, cancellationToken);
    }

    public async Task InterruptSessionAsync(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var context))
        {
            await context.Query.InterruptAsync();

            // Update state to reflect interruption
            var previousState = context.Session.State;
            context.Session.State = SessionState.Cancelled;

            SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs
            {
                Session = context.Session,
                PreviousState = previousState
            });
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

                    // Notify that we have a Claude session ID (for persisting to worktree metadata)
                    if (!string.IsNullOrEmpty(initMsg.SessionId))
                    {
                        ClaudeSessionIdReceived?.Invoke(this, new ClaudeSessionIdReceivedEventArgs
                        {
                            SessionId = context.Session.Id,
                            WorktreeId = context.Session.WorktreeId,
                            ClaudeSessionId = initMsg.SessionId
                        });
                    }
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
