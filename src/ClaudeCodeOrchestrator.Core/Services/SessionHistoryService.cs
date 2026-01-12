using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeCodeOrchestrator.Core.Services;

/// <summary>
/// Service for reading Claude Code session history from disk.
/// </summary>
public sealed class SessionHistoryService
{
    private static readonly string ClaudeProjectsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        "projects");

    /// <summary>
    /// Gets all session IDs for a given worktree path.
    /// </summary>
    public IReadOnlyList<string> GetSessionsForWorktree(string worktreePath)
    {
        var projectDir = GetProjectDirectory(worktreePath);
        if (!Directory.Exists(projectDir))
            return Array.Empty<string>();

        return Directory.GetFiles(projectDir, "*.jsonl")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(name => !name.StartsWith("agent-")) // Filter out agent files
            .OrderByDescending(name => GetSessionLastModified(projectDir, name))
            .ToList();
    }

    /// <summary>
    /// Gets the most recent session ID for a worktree that has actual messages.
    /// </summary>
    public string? GetMostRecentSession(string worktreePath)
    {
        var projectDir = GetProjectDirectory(worktreePath);
        if (!Directory.Exists(projectDir))
            return null;

        // Get sessions ordered by modification time, then find first with actual messages
        var sessions = Directory.GetFiles(projectDir, "*.jsonl")
            .Where(f => !Path.GetFileName(f).StartsWith("agent-"))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        foreach (var sessionFile in sessions)
        {
            if (HasUserOrAssistantMessages(sessionFile))
            {
                return Path.GetFileNameWithoutExtension(sessionFile);
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a session file contains any user or assistant messages.
    /// </summary>
    private static bool HasUserOrAssistantMessages(string sessionFilePath)
    {
        try
        {
            foreach (var line in File.ReadLines(sessionFilePath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Quick check for message types without full parsing
                if (line.Contains("\"type\":\"user\"") || line.Contains("\"type\":\"assistant\""))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignore read errors
        }

        return false;
    }

    /// <summary>
    /// Reads session history messages from a session file.
    /// </summary>
    /// <param name="worktreePath">Path to the worktree.</param>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="maxMessages">Maximum number of messages to load (0 = all). Loads the most recent messages.</param>
    public async Task<IReadOnlyList<SessionHistoryMessage>> ReadSessionHistoryAsync(
        string worktreePath,
        string sessionId,
        CancellationToken cancellationToken = default,
        int maxMessages = 0)
    {
        var projectDir = GetProjectDirectory(worktreePath);
        var sessionFile = Path.Combine(projectDir, $"{sessionId}.jsonl");

        if (!File.Exists(sessionFile))
            return Array.Empty<SessionHistoryMessage>();

        var messages = new List<SessionHistoryMessage>();

        // If maxMessages is specified and file is large, read from the end
        if (maxMessages > 0)
        {
            var fileInfo = new FileInfo(sessionFile);
            // If file is larger than 100KB, use tail-reading approach
            if (fileInfo.Length > 100 * 1024)
            {
                return await ReadLastMessagesAsync(sessionFile, maxMessages, cancellationToken);
            }
        }

        await foreach (var line in File.ReadLinesAsync(sessionFile, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var entry = JsonSerializer.Deserialize<SessionHistoryEntry>(line);
                if (entry is null) continue;

                // Skip non-message entries
                if (entry.Type != "user" && entry.Type != "assistant") continue;

                // Skip sidechain entries (agent tasks)
                if (entry.IsSidechain == true) continue;

                var (content, toolUses) = ExtractContentAndToolUses(entry);

                // Skip if no content and no tool uses
                if (string.IsNullOrEmpty(content) && toolUses.Count == 0) continue;

                messages.Add(new SessionHistoryMessage
                {
                    Role = entry.Type == "user" ? "user" : "assistant",
                    Content = content ?? "",
                    Timestamp = entry.Timestamp,
                    Uuid = entry.Uuid,
                    ToolUses = toolUses
                });
            }
            catch (JsonException)
            {
                // Skip malformed entries
            }
        }

        // If maxMessages specified, return only the last N
        if (maxMessages > 0 && messages.Count > maxMessages)
        {
            return messages.Skip(messages.Count - maxMessages).ToList();
        }

        return messages;
    }

    /// <summary>
    /// Reads the last N messages from a large file by reading from the end.
    /// </summary>
    private async Task<IReadOnlyList<SessionHistoryMessage>> ReadLastMessagesAsync(
        string sessionFile,
        int maxMessages,
        CancellationToken cancellationToken)
    {
        // Read lines from the end of the file
        var lines = new List<string>();
        var messages = new List<SessionHistoryMessage>();

        // Read the file in reverse, collecting enough lines
        // We need to over-read since not all lines are user/assistant messages
        var targetLines = maxMessages * 3; // Read 3x to account for system messages

        await using var stream = new FileStream(sessionFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        // For simplicity with large files, read all lines but process only last N
        // This is still faster than parsing all JSON
        var allLines = new List<string>();
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                allLines.Add(line);
            }
        }

        // Process lines from the end until we have enough messages
        for (var i = allLines.Count - 1; i >= 0 && messages.Count < maxMessages; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var entry = JsonSerializer.Deserialize<SessionHistoryEntry>(allLines[i]);
                if (entry is null) continue;

                // Skip non-message entries
                if (entry.Type != "user" && entry.Type != "assistant") continue;

                // Skip sidechain entries (agent tasks)
                if (entry.IsSidechain == true) continue;

                var (content, toolUses) = ExtractContentAndToolUses(entry);

                // Skip if no content and no tool uses
                if (string.IsNullOrEmpty(content) && toolUses.Count == 0) continue;

                messages.Insert(0, new SessionHistoryMessage
                {
                    Role = entry.Type == "user" ? "user" : "assistant",
                    Content = content ?? "",
                    Timestamp = entry.Timestamp,
                    Uuid = entry.Uuid,
                    ToolUses = toolUses
                });
            }
            catch (JsonException)
            {
                // Skip malformed entries
            }
        }

        return messages;
    }

    private static string GetProjectDirectory(string worktreePath)
    {
        // Claude encodes paths by replacing /, \, ., and : with -
        var encodedPath = worktreePath
            .Replace("/", "-")
            .Replace("\\", "-")
            .Replace(".", "-")
            .Replace(":", "-");

        // Remove leading dash if present (e.g., from paths starting with / on Unix)
        if (encodedPath.StartsWith("-"))
            encodedPath = encodedPath.Substring(1);

        // Directory name is just the encoded path, no leading dash
        return Path.Combine(ClaudeProjectsPath, encodedPath);
    }

    /// <summary>
    /// Debug method to expose the project directory for troubleshooting.
    /// </summary>
    public string GetDebugProjectDirectory(string worktreePath) => GetProjectDirectory(worktreePath);

    private static DateTime GetSessionLastModified(string projectDir, string sessionId)
    {
        var path = Path.Combine(projectDir, $"{sessionId}.jsonl");
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
    }

    private static (string? Content, List<SessionHistoryToolUse> ToolUses) ExtractContentAndToolUses(SessionHistoryEntry entry)
    {
        var toolUses = new List<SessionHistoryToolUse>();

        if (entry.Message is null) return (null, toolUses);

        var contentElement = entry.Message.Content;

        // Skip if content was not set (default JsonElement)
        if (contentElement.ValueKind == JsonValueKind.Undefined)
            return (null, toolUses);

        // For user messages, content might be a string
        if (contentElement.ValueKind == JsonValueKind.String)
        {
            return (contentElement.GetString(), toolUses);
        }

        // For assistant messages, content is an array of content blocks
        if (contentElement.ValueKind == JsonValueKind.Array)
        {
            var textParts = new List<string>();
            foreach (var block in contentElement.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var typeEl))
                {
                    var blockType = typeEl.GetString();

                    // Skip tool_result blocks - these are user message responses
                    if (blockType == "tool_result")
                        continue;

                    // Extract tool_use blocks for assistant messages
                    if (blockType == "tool_use")
                    {
                        var id = block.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                        var name = block.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                        var input = block.TryGetProperty("input", out var inputEl) ? inputEl.ToString() : "{}";

                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        {
                            toolUses.Add(new SessionHistoryToolUse
                            {
                                Id = id,
                                Name = name,
                                InputJson = input ?? "{}"
                            });
                        }
                        continue;
                    }

                    if (blockType == "text" && block.TryGetProperty("text", out var textEl))
                    {
                        var text = textEl.GetString();
                        if (!string.IsNullOrEmpty(text))
                            textParts.Add(text);
                    }
                }
            }
            var content = textParts.Count > 0 ? string.Join("\n", textParts) : null;
            return (content, toolUses);
        }

        return (null, toolUses);
    }
}

/// <summary>
/// Entry from the session JSONL file.
/// </summary>
internal sealed class SessionHistoryEntry
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("isSidechain")]
    public bool? IsSidechain { get; set; }

    [JsonPropertyName("message")]
    public SessionHistoryMessageData? Message { get; set; }
}

internal sealed class SessionHistoryMessageData
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public JsonElement Content { get; set; }
}

/// <summary>
/// A message from session history.
/// </summary>
public sealed class SessionHistoryMessage
{
    public required string Role { get; init; }
    public required string Content { get; init; }
    public string? Timestamp { get; init; }
    public string? Uuid { get; init; }
    public List<SessionHistoryToolUse> ToolUses { get; init; } = new();
}

/// <summary>
/// A tool use from session history.
/// </summary>
public sealed class SessionHistoryToolUse
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string InputJson { get; init; }
}
