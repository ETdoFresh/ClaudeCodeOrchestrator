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
    /// Gets the most recent session ID for a worktree.
    /// </summary>
    public string? GetMostRecentSession(string worktreePath)
    {
        var sessions = GetSessionsForWorktree(worktreePath);
        return sessions.FirstOrDefault();
    }

    /// <summary>
    /// Reads session history messages from a session file.
    /// </summary>
    public async Task<IReadOnlyList<SessionHistoryMessage>> ReadSessionHistoryAsync(
        string worktreePath,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var projectDir = GetProjectDirectory(worktreePath);
        var sessionFile = Path.Combine(projectDir, $"{sessionId}.jsonl");

        if (!File.Exists(sessionFile))
            return Array.Empty<SessionHistoryMessage>();

        var messages = new List<SessionHistoryMessage>();

        await foreach (var line in File.ReadLinesAsync(sessionFile, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var entry = JsonSerializer.Deserialize<SessionHistoryEntry>(line);
                if (entry is null) continue;

                // Skip non-message entries
                if (entry.Type != "user" && entry.Type != "assistant") continue;

                var content = ExtractContent(entry);
                if (string.IsNullOrEmpty(content)) continue;

                messages.Add(new SessionHistoryMessage
                {
                    Role = entry.Type == "user" ? "user" : "assistant",
                    Content = content,
                    Timestamp = entry.Timestamp,
                    Uuid = entry.Uuid
                });
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }

        return messages;
    }

    private static string GetProjectDirectory(string worktreePath)
    {
        // Claude encodes paths by replacing / and . with -
        var encodedPath = worktreePath
            .Replace("/", "-")
            .Replace("\\", "-")
            .Replace(".", "-");

        // Remove leading dash if present
        if (encodedPath.StartsWith("-"))
            encodedPath = encodedPath.Substring(1);

        return Path.Combine(ClaudeProjectsPath, "-" + encodedPath);
    }

    private static DateTime GetSessionLastModified(string projectDir, string sessionId)
    {
        var path = Path.Combine(projectDir, $"{sessionId}.jsonl");
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
    }

    private static string? ExtractContent(SessionHistoryEntry entry)
    {
        if (entry.Message is null) return null;

        // For user messages, content might be a string
        if (entry.Message.Content is JsonElement contentElement)
        {
            if (contentElement.ValueKind == JsonValueKind.String)
            {
                return contentElement.GetString();
            }

            // For assistant messages, content is an array of content blocks
            if (contentElement.ValueKind == JsonValueKind.Array)
            {
                var textParts = new List<string>();
                foreach (var block in contentElement.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var typeEl) &&
                        typeEl.GetString() == "text" &&
                        block.TryGetProperty("text", out var textEl))
                    {
                        var text = textEl.GetString();
                        if (!string.IsNullOrEmpty(text))
                            textParts.Add(text);
                    }
                }
                return string.Join("\n", textParts);
            }
        }

        return null;
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
}
