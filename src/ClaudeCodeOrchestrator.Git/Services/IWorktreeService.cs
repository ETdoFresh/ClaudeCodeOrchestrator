using ClaudeCodeOrchestrator.Git.Models;

namespace ClaudeCodeOrchestrator.Git.Services;

/// <summary>
/// Service for managing Git worktrees.
/// </summary>
public interface IWorktreeService
{
    /// <summary>
    /// Creates a new worktree with a new branch.
    /// </summary>
    Task<WorktreeInfo> CreateWorktreeAsync(
        string repoPath,
        string taskDescription,
        string? baseBranch = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all worktrees for a repository.
    /// </summary>
    Task<IReadOnlyList<WorktreeInfo>> GetWorktreesAsync(
        string repoPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about a specific worktree.
    /// </summary>
    Task<WorktreeInfo?> GetWorktreeAsync(
        string repoPath,
        string worktreeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a worktree.
    /// </summary>
    Task DeleteWorktreeAsync(
        string repoPath,
        string worktreeId,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges a worktree branch into the target branch.
    /// </summary>
    Task<MergeResult> MergeWorktreeAsync(
        string repoPath,
        string worktreeId,
        string targetBranch,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes worktree status information.
    /// </summary>
    Task<WorktreeInfo> RefreshWorktreeStatusAsync(
        string repoPath,
        string worktreeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the Claude session ID for a worktree.
    /// </summary>
    Task UpdateClaudeSessionIdAsync(
        string worktreePath,
        string claudeSessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the SessionWasActive flag for a worktree.
    /// </summary>
    Task UpdateSessionWasActiveAsync(
        string worktreePath,
        bool wasActive,
        CancellationToken cancellationToken = default);
}
