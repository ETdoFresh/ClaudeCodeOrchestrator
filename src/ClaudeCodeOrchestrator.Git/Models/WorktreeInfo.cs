namespace ClaudeCodeOrchestrator.Git.Models;

/// <summary>
/// Information about a Git worktree.
/// </summary>
public sealed record WorktreeInfo
{
    /// <summary>
    /// Unique identifier for this worktree.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Path to the worktree directory.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Branch name associated with this worktree.
    /// </summary>
    public required string BranchName { get; init; }

    /// <summary>
    /// The base branch this worktree was created from.
    /// </summary>
    public required string BaseBranch { get; init; }

    /// <summary>
    /// Human-readable task description.
    /// </summary>
    public required string TaskDescription { get; init; }

    /// <summary>
    /// When the worktree was created.
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// Current status of the worktree.
    /// </summary>
    public WorktreeStatus Status { get; init; }

    /// <summary>
    /// Whether there are uncommitted changes.
    /// </summary>
    public bool HasUncommittedChanges { get; init; }

    /// <summary>
    /// Number of commits ahead of base branch.
    /// </summary>
    public int CommitsAhead { get; init; }

    /// <summary>
    /// The Claude session ID associated with this worktree.
    /// One session per worktree for consistent history.
    /// </summary>
    public string? ClaudeSessionId { get; set; }
}

/// <summary>
/// Status of a worktree.
/// </summary>
public enum WorktreeStatus
{
    /// <summary>
    /// Worktree is active and being used.
    /// </summary>
    Active,

    /// <summary>
    /// Worktree has uncommitted changes.
    /// </summary>
    HasChanges,

    /// <summary>
    /// Worktree is ready to be merged.
    /// </summary>
    ReadyToMerge,

    /// <summary>
    /// Worktree has been merged.
    /// </summary>
    Merged,

    /// <summary>
    /// Worktree is locked.
    /// </summary>
    Locked
}
