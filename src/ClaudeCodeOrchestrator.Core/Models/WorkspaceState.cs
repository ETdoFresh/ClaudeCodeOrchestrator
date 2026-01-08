namespace ClaudeCodeOrchestrator.Core.Models;

/// <summary>
/// Persisted workspace state for a repository.
/// </summary>
public sealed record WorkspaceState
{
    /// <summary>
    /// Version of the workspace state format.
    /// </summary>
    public int Version { get; init; } = 1;

    /// <summary>
    /// When the state was last modified.
    /// </summary>
    public DateTime LastModified { get; set; }

    /// <summary>
    /// Serialized dock layout.
    /// </summary>
    public string? DockLayout { get; init; }

    /// <summary>
    /// Session snapshots for resuming.
    /// </summary>
    public List<SessionSnapshot> Sessions { get; init; } = new();

    /// <summary>
    /// Worktree snapshots.
    /// </summary>
    public List<WorktreeSnapshot> Worktrees { get; init; } = new();

    /// <summary>
    /// Last opened file path.
    /// </summary>
    public string? LastOpenFilePath { get; set; }
}

/// <summary>
/// Snapshot of a session for persistence.
/// </summary>
public sealed record SessionSnapshot
{
    /// <summary>
    /// Internal session ID.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Claude Code session ID.
    /// </summary>
    public required string ClaudeSessionId { get; init; }

    /// <summary>
    /// Associated worktree ID.
    /// </summary>
    public required string WorktreeId { get; init; }

    /// <summary>
    /// State when snapshot was taken.
    /// </summary>
    public required SessionState State { get; init; }

    /// <summary>
    /// When the session was created.
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// When the session ended.
    /// </summary>
    public DateTime? EndedAt { get; init; }

    /// <summary>
    /// Total cost in USD.
    /// </summary>
    public decimal TotalCostUsd { get; init; }

    /// <summary>
    /// Whether this session can be resumed.
    /// </summary>
    public bool IsResumable { get; init; }

    /// <summary>
    /// Initial prompt.
    /// </summary>
    public required string InitialPrompt { get; init; }
}

/// <summary>
/// Snapshot of a worktree for persistence.
/// </summary>
public sealed record WorktreeSnapshot
{
    /// <summary>
    /// Worktree ID.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Path to the worktree.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Branch name.
    /// </summary>
    public required string BranchName { get; init; }

    /// <summary>
    /// Base branch.
    /// </summary>
    public required string BaseBranch { get; init; }

    /// <summary>
    /// Task description.
    /// </summary>
    public required string TaskDescription { get; init; }
}
