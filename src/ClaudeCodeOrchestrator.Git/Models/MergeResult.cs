namespace ClaudeCodeOrchestrator.Git.Models;

/// <summary>
/// Result of a merge operation.
/// </summary>
public sealed record MergeResult
{
    /// <summary>
    /// Whether the merge was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The merge status.
    /// </summary>
    public required MergeStatus Status { get; init; }

    /// <summary>
    /// Commit SHA of the merge commit (if successful).
    /// </summary>
    public string? MergeCommitSha { get; init; }

    /// <summary>
    /// List of conflicting files (if any).
    /// </summary>
    public IReadOnlyList<string>? ConflictingFiles { get; init; }

    /// <summary>
    /// Error message (if failed).
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Status of a merge operation.
/// </summary>
public enum MergeStatus
{
    /// <summary>
    /// Merge completed successfully with a merge commit.
    /// </summary>
    MergeCommit,

    /// <summary>
    /// Fast-forward merge (no merge commit needed).
    /// </summary>
    FastForward,

    /// <summary>
    /// Already up to date, no merge needed.
    /// </summary>
    UpToDate,

    /// <summary>
    /// Merge has conflicts that need resolution.
    /// </summary>
    Conflicts,

    /// <summary>
    /// Merge failed for other reasons.
    /// </summary>
    Failed
}
