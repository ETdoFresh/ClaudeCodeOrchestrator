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
    /// Human-readable task description (the original prompt).
    /// </summary>
    public required string TaskDescription { get; init; }

    /// <summary>
    /// Generated title for display (summarizes the task).
    /// </summary>
    public string? Title { get; init; }

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
    /// Number of commits ahead of base branch (used for merge readiness).
    /// </summary>
    public int CommitsAhead { get; init; }

    /// <summary>
    /// Number of unpushed commits (ahead of remote tracking branch).
    /// </summary>
    public int UnpushedCommits { get; init; }

    /// <summary>
    /// The Claude session ID associated with this worktree.
    /// One session per worktree for consistent history.
    /// </summary>
    public string? ClaudeSessionId { get; set; }

    /// <summary>
    /// Whether the session was actively running when the app last closed.
    /// Used to restore sessions on app restart.
    /// </summary>
    public bool SessionWasActive { get; set; }

    /// <summary>
    /// Accumulated active processing duration in milliseconds.
    /// This tracks only the time Claude was actively processing, not idle time.
    /// </summary>
    public long AccumulatedDurationMs { get; set; }

    /// <summary>
    /// Accumulated total cost in USD across all session resumes.
    /// </summary>
    public decimal AccumulatedCostUsd { get; set; }

    /// <summary>
    /// Whether this worktree was used as a job (for persistence across app restarts).
    /// </summary>
    public bool WasJob { get; set; }

    /// <summary>
    /// The last iteration number when the job was running (for display in history).
    /// </summary>
    public int? LastIteration { get; set; }

    /// <summary>
    /// The max iterations configured when the job was running (for display in history).
    /// </summary>
    public int? JobMaxIterations { get; set; }
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
