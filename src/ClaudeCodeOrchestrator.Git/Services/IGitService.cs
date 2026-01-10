using ClaudeCodeOrchestrator.Git.Models;

namespace ClaudeCodeOrchestrator.Git.Services;

/// <summary>
/// Service for Git operations.
/// </summary>
public interface IGitService
{
    /// <summary>
    /// Opens a repository at the specified path.
    /// </summary>
    Task<RepositoryInfo> OpenRepositoryAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initializes a new Git repository at the specified path.
    /// </summary>
    /// <param name="path">The path where the repository should be initialized.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Information about the newly initialized repository.</returns>
    Task<RepositoryInfo> InitializeRepositoryAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all branches in the repository.
    /// </summary>
    Task<IReadOnlyList<string>> GetBranchesAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current branch name.
    /// </summary>
    Task<string> GetCurrentBranchAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the diff between two refs (or working tree if toRef is null).
    /// </summary>
    Task<IReadOnlyList<DiffEntry>> GetDiffAsync(
        string repoPath,
        string? fromRef = null,
        string? toRef = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets uncommitted changes in the working tree.
    /// </summary>
    Task<IReadOnlyList<DiffEntry>> GetUncommittedChangesAsync(
        string repoPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a line is present in .gitignore.
    /// </summary>
    Task EnsureGitIgnoreEntryAsync(
        string repoPath,
        string entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the number of commits a branch is ahead of another.
    /// </summary>
    Task<int> GetCommitsAheadAsync(
        string repoPath,
        string branch,
        string baseBranch,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the number of commits ahead of the remote tracking branch (unpushed commits).
    /// </summary>
    Task<int> GetCommitsAheadOfRemoteAsync(
        string repoPath,
        string branch,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a branch exists.
    /// </summary>
    Task<bool> BranchExistsAsync(
        string repoPath,
        string branchName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes all local branches to the remote.
    /// </summary>
    Task PushAllBranchesAsync(
        string repoPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls changes from the remote for the current branch.
    /// </summary>
    Task PullAsync(
        string repoPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the diff entries between two directory paths by comparing file contents.
    /// </summary>
    /// <param name="basePath">The base directory path (e.g., local repository).</param>
    /// <param name="comparePath">The directory to compare against (e.g., worktree).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of diff entries representing changed files.</returns>
    Task<IReadOnlyList<DiffEntry>> GetDiffEntriesBetweenPathsAsync(
        string basePath,
        string comparePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the remote URL for the origin remote.
    /// </summary>
    /// <param name="repoPath">The repository path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The remote URL, or null if no origin remote is configured.</returns>
    Task<string?> GetRemoteUrlAsync(
        string repoPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the repository has any configured remotes.
    /// </summary>
    /// <param name="repoPath">Path to the repository.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the repository has at least one remote configured.</returns>
    Task<bool> HasRemoteAsync(
        string repoPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the number of commits behind the remote tracking branch (commits to pull).
    /// </summary>
    /// <param name="repoPath">Path to the repository.</param>
    /// <param name="branch">The branch name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of commits behind the remote, or 0 if no remote or error.</returns>
    Task<int> GetCommitsBehindRemoteAsync(
        string repoPath,
        string branch,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls changes from the remote repository.
    /// </summary>
    /// <param name="repoPath">Path to the repository.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PullAsync(
        string repoPath,
        CancellationToken cancellationToken = default);
}
