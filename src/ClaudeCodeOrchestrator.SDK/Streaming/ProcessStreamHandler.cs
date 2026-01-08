using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ClaudeCodeOrchestrator.SDK.Messages;
using ClaudeCodeOrchestrator.SDK.Options;

namespace ClaudeCodeOrchestrator.SDK.Streaming;

/// <summary>
/// Handles spawning and communicating with the Claude Code process.
/// </summary>
internal sealed class ProcessStreamHandler : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StreamReader _stdout;
    private readonly StreamWriter _stdin;
    private readonly CancellationTokenSource _cts;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    private ProcessStreamHandler(Process process, JsonSerializerOptions jsonOptions)
    {
        _process = process;
        _stdout = process.StandardOutput;
        _stdin = process.StandardInput;
        _cts = new CancellationTokenSource();
        _jsonOptions = jsonOptions;
    }

    /// <summary>
    /// Starts a new Claude Code process with the given prompt and options.
    /// </summary>
    public static ProcessStreamHandler Start(
        string prompt,
        ClaudeAgentOptions? options = null)
    {
        options ??= new ClaudeAgentOptions();
        var claudePath = FindClaudeExecutable(options);
        var args = BuildArguments(prompt, options);

        var startInfo = new ProcessStartInfo
        {
            FileName = claudePath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = options.Cwd ?? Environment.CurrentDirectory
        };

        // Add environment variables
        if (options.Environment != null)
        {
            foreach (var (key, value) in options.Environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start Claude Code process");
            }
        }
        catch (Exception ex)
        {
            process.Dispose();
            throw new InvalidOperationException($"Failed to start Claude Code: {ex.Message}", ex);
        }

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        return new ProcessStreamHandler(process, jsonOptions);
    }

    /// <summary>
    /// Starts a new Claude Code process for multi-turn conversation (streaming input mode).
    /// </summary>
    public static ProcessStreamHandler StartStreaming(ClaudeAgentOptions? options = null)
    {
        options ??= new ClaudeAgentOptions();
        var claudePath = FindClaudeExecutable(options);
        var args = BuildStreamingArguments(options);

        var startInfo = new ProcessStartInfo
        {
            FileName = claudePath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
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

        var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start Claude Code process");
            }
        }
        catch (Exception ex)
        {
            process.Dispose();
            throw new InvalidOperationException($"Failed to start Claude Code: {ex.Message}", ex);
        }

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        return new ProcessStreamHandler(process, jsonOptions);
    }

    /// <summary>
    /// Reads messages from Claude Code stdout as they arrive.
    /// </summary>
    public async IAsyncEnumerable<ISDKMessage> ReadMessagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        var token = linkedCts.Token;

        while (!_process.HasExited && !token.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await _stdout.ReadLineAsync(token);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            if (line == null)
            {
                // End of stream
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var message = ParseMessage(line);
            if (message != null)
            {
                yield return message;

                // Stop reading after result message
                if (message is SDKResultMessage)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Sends a user message to Claude Code stdin.
    /// </summary>
    public async Task SendMessageAsync(SDKUserMessage message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message, _jsonOptions);
        await _stdin.WriteLineAsync(json.AsMemory(), cancellationToken);
        await _stdin.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Sends an interrupt signal to Claude Code.
    /// </summary>
    public async Task SendInterruptAsync(CancellationToken cancellationToken = default)
    {
        var interrupt = new { type = "interrupt" };
        var json = JsonSerializer.Serialize(interrupt, _jsonOptions);
        await _stdin.WriteLineAsync(json.AsMemory(), cancellationToken);
        await _stdin.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Sends a command to Claude Code (for advanced control).
    /// </summary>
    public async Task SendCommandAsync(string command, object? payload = null, CancellationToken cancellationToken = default)
    {
        var cmd = new Dictionary<string, object?> { ["type"] = command };
        if (payload != null)
        {
            cmd["payload"] = payload;
        }

        var json = JsonSerializer.Serialize(cmd, _jsonOptions);
        await _stdin.WriteLineAsync(json.AsMemory(), cancellationToken);
        await _stdin.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Closes stdin to signal end of input.
    /// </summary>
    public void CloseInput()
    {
        _stdin.Close();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _cts.CancelAsync();

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore errors during cleanup
        }

        _process.Dispose();
        _cts.Dispose();
    }

    private ISDKMessage? ParseMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeElement))
            {
                return null;
            }

            var type = typeElement.GetString();
            return type switch
            {
                "assistant" => JsonSerializer.Deserialize<SDKAssistantMessage>(json, _jsonOptions),
                "user" => JsonSerializer.Deserialize<SDKUserMessage>(json, _jsonOptions),
                "result" => JsonSerializer.Deserialize<SDKResultMessage>(json, _jsonOptions),
                "system" => JsonSerializer.Deserialize<SDKSystemMessage>(json, _jsonOptions),
                "stream_event" => JsonSerializer.Deserialize<SDKStreamEvent>(json, _jsonOptions),
                _ => null // Unknown message type
            };
        }
        catch (JsonException)
        {
            // Failed to parse, skip this message
            return null;
        }
    }

    private static string FindClaudeExecutable(ClaudeAgentOptions options)
    {
        if (!string.IsNullOrEmpty(options.PathToClaudeCodeExecutable))
        {
            return options.PathToClaudeCodeExecutable;
        }

        // Try to find 'claude' in PATH
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        foreach (var path in paths)
        {
            var claudePath = Path.Combine(path, "claude");
            if (File.Exists(claudePath))
            {
                return claudePath;
            }

            // On Windows, check for .cmd and .exe
            if (OperatingSystem.IsWindows())
            {
                var claudeCmd = Path.Combine(path, "claude.cmd");
                if (File.Exists(claudeCmd)) return claudeCmd;

                var claudeExe = Path.Combine(path, "claude.exe");
                if (File.Exists(claudeExe)) return claudeExe;
            }
        }

        // Default to just 'claude' and hope it's in PATH
        return "claude";
    }

    private static string BuildArguments(string prompt, ClaudeAgentOptions options)
    {
        var args = new List<string>
        {
            "--output-format", "stream-json",
            "--verbose",
            "-p", EscapeArgument(prompt)
        };

        AddCommonArguments(args, options);

        return string.Join(" ", args);
    }

    private static string BuildStreamingArguments(ClaudeAgentOptions options)
    {
        var args = new List<string>
        {
            "--output-format", "stream-json",
            "--input-format", "stream-json",
            "--verbose"
        };

        AddCommonArguments(args, options);

        return string.Join(" ", args);
    }

    private static void AddCommonArguments(List<string> args, ClaudeAgentOptions options)
    {
        if (!string.IsNullOrEmpty(options.Resume))
        {
            args.Add("--resume");
            args.Add(options.Resume);
        }

        if (options.Continue)
        {
            args.Add("--continue");
        }

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

        if (options.PermissionMode == PermissionMode.AcceptAll)
        {
            args.Add("--dangerously-skip-permissions");
        }

        if (options.AdditionalDirectories != null)
        {
            foreach (var dir in options.AdditionalDirectories)
            {
                args.Add("--add-dir");
                args.Add(EscapeArgument(dir));
            }
        }

        if (options.AllowedTools != null)
        {
            foreach (var tool in options.AllowedTools)
            {
                args.Add("--allowedTools");
                args.Add(tool);
            }
        }

        if (options.DisallowedTools != null)
        {
            foreach (var tool in options.DisallowedTools)
            {
                args.Add("--disallowedTools");
                args.Add(tool);
            }
        }

        if (options.SystemPrompt?.Append != null)
        {
            args.Add("--append-system-prompt");
            args.Add(EscapeArgument(options.SystemPrompt.Append));
        }

        if (options.ExecutableArgs != null)
        {
            args.AddRange(options.ExecutableArgs);
        }
    }

    private static string EscapeArgument(string arg)
    {
        // Simple escaping - wrap in quotes if contains spaces
        if (arg.Contains(' ') || arg.Contains('"'))
        {
            return $"\"{arg.Replace("\"", "\\\"")}\"";
        }
        return arg;
    }
}
