using System.Diagnostics;
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
    /// Executes a simple one-off query and returns the text response.
    /// This is optimized for quick, single-turn requests that don't need streaming or tool use.
    /// Uses --print mode for minimal overhead.
    /// </summary>
    /// <param name="prompt">The prompt to send to Claude Code.</param>
    /// <param name="options">Optional configuration options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The text response from Claude, or null if the query failed.</returns>
    public static async Task<string?> QueryOnceAsync(
        string prompt,
        ClaudeAgentOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ClaudeAgentOptions();
        var claudePath = FindClaudeExecutable(options);
        var args = BuildPrintArguments(prompt, options);

        var startInfo = new ProcessStartInfo
        {
            FileName = claudePath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = options.Cwd ?? Environment.CurrentDirectory
        };

        if (options.Environment != null)
        {
            foreach (var (key, value) in options.Environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            var output = await outputTask;
            var error = await errorTask;

            if (!string.IsNullOrEmpty(error))
            {
                Console.Error.WriteLine($"[SDK QueryOnce] stderr: {error}");
            }

            if (process.ExitCode != 0)
            {
                Console.Error.WriteLine($"[SDK QueryOnce] Exit code: {process.ExitCode}");
                return null;
            }

            return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SDK QueryOnce] Error: {ex.Message}");
            return null;
        }
    }

    private static string FindClaudeExecutable(ClaudeAgentOptions options)
    {
        if (!string.IsNullOrEmpty(options.PathToClaudeCodeExecutable))
        {
            return options.PathToClaudeCodeExecutable;
        }

        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        foreach (var path in paths)
        {
            var claudePath = Path.Combine(path, "claude");
            if (File.Exists(claudePath))
            {
                return claudePath;
            }

            if (OperatingSystem.IsWindows())
            {
                var claudeCmd = Path.Combine(path, "claude.cmd");
                if (File.Exists(claudeCmd)) return claudeCmd;

                var claudeExe = Path.Combine(path, "claude.exe");
                if (File.Exists(claudeExe)) return claudeExe;
            }
        }

        return "claude";
    }

    private static string BuildPrintArguments(string prompt, ClaudeAgentOptions options)
    {
        var args = new List<string> { "--print" };

        if (!string.IsNullOrEmpty(options.Model))
        {
            args.Add("--model");
            args.Add(options.Model);
        }

        if (options.MaxTurns.HasValue)
        {
            args.Add("--max-turns");
            args.Add(options.MaxTurns.Value.ToString());
        }

        if (options.PermissionMode == PermissionMode.Plan)
        {
            args.Add("--allowedTools");
            args.Add("\"[]\"");
        }
        else if (options.PermissionMode == PermissionMode.AcceptAll)
        {
            args.Add("--dangerously-skip-permissions");
        }

        // Escape prompt for shell
        var escapedPrompt = prompt.Replace("\"", "\\\"");
        args.Add($"\"{escapedPrompt}\"");

        return string.Join(" ", args);
    }

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
