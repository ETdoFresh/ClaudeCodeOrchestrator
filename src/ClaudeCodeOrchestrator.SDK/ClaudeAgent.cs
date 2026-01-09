using System.Runtime.CompilerServices;
using ClaudeCodeOrchestrator.SDK.Messages;
using ClaudeCodeOrchestrator.SDK.Options;
using ClaudeCodeOrchestrator.SDK.Streaming;

namespace ClaudeCodeOrchestrator.SDK;

/// <summary>
/// Main entry point for interacting with Claude Code.
/// </summary>
public static class ClaudeAgent
{
    /// <summary>
    /// Creates an async enumerable that streams messages from Claude Code.
    /// This is a convenience method for simple one-shot queries.
    /// </summary>
    /// <param name="prompt">The prompt to send to Claude Code.</param>
    /// <param name="options">Optional configuration options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of SDK messages.</returns>
    public static async IAsyncEnumerable<ISDKMessage> QueryAsync(
        string prompt,
        ClaudeAgentOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var handler = ProcessStreamHandler.Start(prompt, options);

        try
        {
            await foreach (var message in handler.ReadMessagesAsync(cancellationToken))
            {
                yield return message;
            }
        }
        finally
        {
            await handler.DisposeAsync();
        }
    }

    /// <summary>
    /// Creates a Query object for more advanced control over the Claude Code session.
    /// Use this when you need to send follow-up messages, interrupt, or use other control methods.
    /// </summary>
    /// <param name="prompt">The initial prompt to send to Claude Code.</param>
    /// <param name="options">Optional configuration options.</param>
    /// <returns>A Query object that can be enumerated and controlled.</returns>
    public static Query CreateQuery(string prompt, ClaudeAgentOptions? options = null)
    {
        var handler = ProcessStreamHandler.Start(prompt, options);
        return new Query(handler);
    }

    /// <summary>
    /// Creates a Query object with images for more advanced control over the Claude Code session.
    /// </summary>
    /// <param name="prompt">The initial prompt to send to Claude Code.</param>
    /// <param name="images">Images to include with the prompt.</param>
    /// <param name="options">Optional configuration options.</param>
    /// <returns>A Query object that can be enumerated and controlled.</returns>
    public static Query CreateQuery(string prompt, IReadOnlyList<ImageContentBlock> images, ClaudeAgentOptions? options = null)
    {
        var handler = ProcessStreamHandler.Start(prompt, images, options);
        return new Query(handler);
    }

    /// <summary>
    /// Creates a Query object with streaming input support for multi-turn conversations.
    /// Use this when you need to send multiple prompts in a conversation.
    /// </summary>
    /// <param name="options">Optional configuration options.</param>
    /// <returns>A Query object that supports sending multiple messages.</returns>
    public static Query CreateStreamingQuery(ClaudeAgentOptions? options = null)
    {
        var handler = ProcessStreamHandler.StartStreaming(options);
        return new Query(handler);
    }

    /// <summary>
    /// Resumes a previous Claude Code session.
    /// </summary>
    /// <param name="sessionId">The session ID to resume.</param>
    /// <param name="prompt">Optional prompt to continue with.</param>
    /// <param name="options">Optional configuration options (Resume will be set automatically).</param>
    /// <returns>A Query object for the resumed session.</returns>
    public static Query ResumeSession(string sessionId, string? prompt = null, ClaudeAgentOptions? options = null)
    {
        options ??= new ClaudeAgentOptions();
        options = options with { Resume = sessionId };

        if (!string.IsNullOrEmpty(prompt))
        {
            return CreateQuery(prompt, options);
        }
        else
        {
            return CreateStreamingQuery(options);
        }
    }

    /// <summary>
    /// Continues the most recent Claude Code session.
    /// </summary>
    /// <param name="prompt">Optional prompt to continue with.</param>
    /// <param name="options">Optional configuration options (Continue will be set automatically).</param>
    /// <returns>A Query object for the continued session.</returns>
    public static Query ContinueSession(string? prompt = null, ClaudeAgentOptions? options = null)
    {
        options ??= new ClaudeAgentOptions();
        options = options with { Continue = true };

        if (!string.IsNullOrEmpty(prompt))
        {
            return CreateQuery(prompt, options);
        }
        else
        {
            return CreateStreamingQuery(options);
        }
    }
}
