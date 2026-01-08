namespace ClaudeCodeOrchestrator.Git.Models;

/// <summary>
/// Information about a Git repository.
/// </summary>
public sealed record RepositoryInfo
{
    /// <summary>
    /// Path to the repository root.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Name of the repository (folder name).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Default branch name (e.g., "main" or "master").
    /// </summary>
    public required string DefaultBranch { get; init; }

    /// <summary>
    /// Current branch name.
    /// </summary>
    public required string CurrentBranch { get; init; }

    /// <summary>
    /// Path to the worktrees directory.
    /// </summary>
    public required string WorktreesDirectory { get; init; }

    /// <summary>
    /// Whether this is inside a worktree (vs the main repo).
    /// </summary>
    public bool IsWorktree { get; init; }
}
