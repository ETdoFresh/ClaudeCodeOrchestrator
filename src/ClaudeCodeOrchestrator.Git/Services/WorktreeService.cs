using System.Text.Json;
using ClaudeCodeOrchestrator.Git.Models;
using LibGit2Sharp;
using MergeResultModel = ClaudeCodeOrchestrator.Git.Models.MergeResult;
using MergeStatusModel = ClaudeCodeOrchestrator.Git.Models.MergeStatus;

namespace ClaudeCodeOrchestrator.Git.Services;

/// <summary>
/// Service for managing Git worktrees.
/// </summary>
public sealed class WorktreeService : IWorktreeService
{
    private const string WorktreesDirectoryName = ".worktrees";
    private const string MetadataFileName = ".worktree-metadata.json";

    private readonly IGitService _gitService;
    private readonly BranchNameGenerator _branchNameGenerator;

    public WorktreeService(IGitService gitService, BranchNameGenerator branchNameGenerator)
    {
        _gitService = gitService;
        _branchNameGenerator = branchNameGenerator;
    }

    public async Task<WorktreeInfo> CreateWorktreeAsync(
        string repoPath,
        string taskDescription,
        string? baseBranch = null,
        CancellationToken cancellationToken = default)
    {
        var repoInfo = await _gitService.OpenRepositoryAsync(repoPath, cancellationToken);
        baseBranch ??= repoInfo.DefaultBranch;

        // Generate branch name
        var branchName = _branchNameGenerator.Generate(taskDescription);

        // Ensure worktrees directory exists and is gitignored
        var worktreesDir = Path.Combine(repoPath, WorktreesDirectoryName);
        Directory.CreateDirectory(worktreesDir);
        await _gitService.EnsureGitIgnoreEntryAsync(repoPath, WorktreesDirectoryName, cancellationToken);

        // Create worktree path
        var worktreeId = Guid.NewGuid().ToString("N")[..8];
        var worktreePath = Path.Combine(worktreesDir, $"{branchName.Replace("task/", "")}-{worktreeId}");

        // Create worktree using git command (LibGit2Sharp worktree support is limited)
        await CreateWorktreeViaGitAsync(repoPath, branchName, worktreePath, baseBranch, cancellationToken);

        // Save metadata
        var metadata = new WorktreeMetadata
        {
            Id = worktreeId,
            TaskDescription = taskDescription,
            BaseBranch = baseBranch,
            CreatedAt = DateTime.UtcNow
        };

        var metadataPath = Path.Combine(worktreePath, MetadataFileName);
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, json, cancellationToken);

        return new WorktreeInfo
        {
            Id = worktreeId,
            Path = worktreePath,
            BranchName = branchName,
            BaseBranch = baseBranch,
            TaskDescription = taskDescription,
            CreatedAt = metadata.CreatedAt,
            Status = WorktreeStatus.Active,
            HasUncommittedChanges = false,
            CommitsAhead = 0
        };
    }

    public async Task<IReadOnlyList<WorktreeInfo>> GetWorktreesAsync(
        string repoPath,
        CancellationToken cancellationToken = default)
    {
        var worktreesDir = Path.Combine(repoPath, WorktreesDirectoryName);
        if (!Directory.Exists(worktreesDir))
        {
            return Array.Empty<WorktreeInfo>();
        }

        var worktrees = new List<WorktreeInfo>();
        var directories = Directory.GetDirectories(worktreesDir);

        foreach (var dir in directories)
        {
            var worktree = await LoadWorktreeAsync(repoPath, dir, cancellationToken);
            if (worktree != null)
            {
                worktrees.Add(worktree);
            }
        }

        return worktrees.OrderByDescending(w => w.CreatedAt).ToList();
    }

    public async Task<WorktreeInfo?> GetWorktreeAsync(
        string repoPath,
        string worktreeId,
        CancellationToken cancellationToken = default)
    {
        var worktreesDir = Path.Combine(repoPath, WorktreesDirectoryName);
        if (!Directory.Exists(worktreesDir))
        {
            return null;
        }

        var directories = Directory.GetDirectories(worktreesDir);
        foreach (var dir in directories)
        {
            var worktree = await LoadWorktreeAsync(repoPath, dir, cancellationToken);
            if (worktree?.Id == worktreeId)
            {
                return worktree;
            }
        }

        return null;
    }

    public async Task DeleteWorktreeAsync(
        string repoPath,
        string worktreeId,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var worktree = await GetWorktreeAsync(repoPath, worktreeId, cancellationToken)
            ?? throw new InvalidOperationException($"Worktree {worktreeId} not found");

        // Remove worktree using git command
        await RemoveWorktreeViaGitAsync(repoPath, worktree.Path, force, cancellationToken);

        // Delete the directory if it still exists
        if (Directory.Exists(worktree.Path))
        {
            Directory.Delete(worktree.Path, recursive: true);
        }

        // Optionally delete the branch
        // For now, we'll keep the branch in case user wants to reference it
    }

    public async Task<MergeResultModel> MergeWorktreeAsync(
        string repoPath,
        string worktreeId,
        string targetBranch,
        CancellationToken cancellationToken = default)
    {
        var worktree = await GetWorktreeAsync(repoPath, worktreeId, cancellationToken)
            ?? throw new InvalidOperationException($"Worktree {worktreeId} not found");

        // Use git merge command
        return await MergeViaGitAsync(repoPath, worktree.BranchName, targetBranch, cancellationToken);
    }

    public async Task<WorktreeInfo> RefreshWorktreeStatusAsync(
        string repoPath,
        string worktreeId,
        CancellationToken cancellationToken = default)
    {
        var worktree = await GetWorktreeAsync(repoPath, worktreeId, cancellationToken)
            ?? throw new InvalidOperationException($"Worktree {worktreeId} not found");

        return worktree;
    }

    private async Task<WorktreeInfo?> LoadWorktreeAsync(
        string repoPath,
        string worktreePath,
        CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(worktreePath, MetadataFileName);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
            var metadata = JsonSerializer.Deserialize<WorktreeMetadata>(json);
            if (metadata == null)
            {
                return null;
            }

            // Get current branch from worktree
            var branchName = await GetWorktreeBranchAsync(worktreePath, cancellationToken);
            if (branchName == null)
            {
                return null;
            }

            // Check for uncommitted changes (excluding our metadata file)
            var changes = await _gitService.GetUncommittedChangesAsync(worktreePath, cancellationToken);
            var hasUncommittedChanges = changes.Any(c => !c.FilePath.EndsWith(MetadataFileName));

            // Get commits ahead
            var commitsAhead = await _gitService.GetCommitsAheadAsync(
                worktreePath, branchName, metadata.BaseBranch, cancellationToken);

            var status = hasUncommittedChanges
                ? WorktreeStatus.HasChanges
                : commitsAhead > 0
                    ? WorktreeStatus.ReadyToMerge
                    : WorktreeStatus.Active;

            return new WorktreeInfo
            {
                Id = metadata.Id,
                Path = worktreePath,
                BranchName = branchName,
                BaseBranch = metadata.BaseBranch,
                TaskDescription = metadata.TaskDescription,
                CreatedAt = metadata.CreatedAt,
                Status = status,
                HasUncommittedChanges = hasUncommittedChanges,
                CommitsAhead = commitsAhead,
                ClaudeSessionId = metadata.ClaudeSessionId
            };
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> GetWorktreeBranchAsync(string worktreePath, CancellationToken cancellationToken)
    {
        try
        {
            var headPath = Path.Combine(worktreePath, ".git");
            if (File.Exists(headPath))
            {
                // Worktree .git is a file pointing to the actual git dir
                var content = await File.ReadAllTextAsync(headPath, cancellationToken);
                if (content.StartsWith("gitdir:"))
                {
                    var gitDir = content["gitdir:".Length..].Trim();
                    var headFile = Path.Combine(gitDir, "HEAD");
                    if (File.Exists(headFile))
                    {
                        var head = await File.ReadAllTextAsync(headFile, cancellationToken);
                        if (head.StartsWith("ref: refs/heads/"))
                        {
                            return head["ref: refs/heads/".Length..].Trim();
                        }
                    }
                }
            }

            // Fallback: use Repository
            using var repo = new Repository(worktreePath);
            return repo.Head.FriendlyName;
        }
        catch
        {
            return null;
        }
    }

    private static async Task CreateWorktreeViaGitAsync(
        string repoPath,
        string branchName,
        string worktreePath,
        string baseBranch,
        CancellationToken cancellationToken)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"worktree add -b \"{branchName}\" \"{worktreePath}\" \"{baseBranch}\"",
            WorkingDirectory = repoPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process");

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to create worktree: {error}");
        }
    }

    private static async Task RemoveWorktreeViaGitAsync(
        string repoPath,
        string worktreePath,
        bool force,
        CancellationToken cancellationToken)
    {
        var forceFlag = force ? "--force" : "";
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"worktree remove {forceFlag} \"{worktreePath}\"",
            WorkingDirectory = repoPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process");

        await process.WaitForExitAsync(cancellationToken);

        // Ignore errors - we'll delete the directory manually if needed
    }

    private static async Task<MergeResultModel> MergeViaGitAsync(
        string repoPath,
        string sourceBranch,
        string targetBranch,
        CancellationToken cancellationToken)
    {
        // First checkout target branch
        var checkoutPsi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"checkout \"{targetBranch}\"",
            WorkingDirectory = repoPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var checkoutProcess = System.Diagnostics.Process.Start(checkoutPsi)
            ?? throw new InvalidOperationException("Failed to start git process");

        await checkoutProcess.WaitForExitAsync(cancellationToken);

        if (checkoutProcess.ExitCode != 0)
        {
            var error = await checkoutProcess.StandardError.ReadToEndAsync(cancellationToken);
            return new MergeResultModel
            {
                Success = false,
                Status = MergeStatusModel.Failed,
                ErrorMessage = $"Failed to checkout {targetBranch}: {error}"
            };
        }

        // Then merge
        var mergePsi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"merge \"{sourceBranch}\" --no-edit",
            WorkingDirectory = repoPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var mergeProcess = System.Diagnostics.Process.Start(mergePsi)
            ?? throw new InvalidOperationException("Failed to start git process");

        await mergeProcess.WaitForExitAsync(cancellationToken);
        var output = await mergeProcess.StandardOutput.ReadToEndAsync(cancellationToken);

        if (mergeProcess.ExitCode == 0)
        {
            var status = output.Contains("Fast-forward")
                ? MergeStatusModel.FastForward
                : output.Contains("Already up to date")
                    ? MergeStatusModel.UpToDate
                    : MergeStatusModel.MergeCommit;

            return new MergeResultModel
            {
                Success = true,
                Status = status
            };
        }
        else
        {
            var errorOutput = await mergeProcess.StandardError.ReadToEndAsync(cancellationToken);
            var hasConflicts = errorOutput.Contains("CONFLICT") || output.Contains("CONFLICT");

            // If there are conflicts, abort the merge to leave the repo in a clean state
            if (hasConflicts)
            {
                var conflictingFiles = ParseConflictingFiles(output + errorOutput);
                await AbortMergeAsync(repoPath, cancellationToken);

                return new MergeResultModel
                {
                    Success = false,
                    Status = MergeStatusModel.Conflicts,
                    ConflictingFiles = conflictingFiles,
                    ErrorMessage = "Merge conflicts detected"
                };
            }

            return new MergeResultModel
            {
                Success = false,
                Status = MergeStatusModel.Failed,
                ErrorMessage = errorOutput
            };
        }
    }

    private static async Task AbortMergeAsync(string repoPath, CancellationToken cancellationToken)
    {
        var abortPsi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = "merge --abort",
            WorkingDirectory = repoPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var abortProcess = System.Diagnostics.Process.Start(abortPsi);
        if (abortProcess != null)
        {
            await abortProcess.WaitForExitAsync(cancellationToken);
        }
    }

    private static List<string> ParseConflictingFiles(string output)
    {
        var files = new List<string>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            // Parse lines like "CONFLICT (content): Merge conflict in filename.cs"
            if (line.Contains("CONFLICT") && line.Contains("Merge conflict in "))
            {
                var idx = line.IndexOf("Merge conflict in ", StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var file = line[(idx + "Merge conflict in ".Length)..].Trim();
                    if (!string.IsNullOrEmpty(file))
                    {
                        files.Add(file);
                    }
                }
            }
        }

        return files;
    }

    private sealed record WorktreeMetadata
    {
        public required string Id { get; init; }
        public required string TaskDescription { get; init; }
        public required string BaseBranch { get; init; }
        public required DateTime CreatedAt { get; init; }
        public string? ClaudeSessionId { get; init; }
    }

    /// <summary>
    /// Updates the Claude session ID for a worktree.
    /// </summary>
    public async Task UpdateClaudeSessionIdAsync(
        string worktreePath,
        string claudeSessionId,
        CancellationToken cancellationToken = default)
    {
        var metadataPath = Path.Combine(worktreePath, MetadataFileName);
        if (!File.Exists(metadataPath))
            return;

        var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
        var metadata = JsonSerializer.Deserialize<WorktreeMetadata>(json);
        if (metadata == null)
            return;

        var updatedMetadata = metadata with { ClaudeSessionId = claudeSessionId };
        var updatedJson = JsonSerializer.Serialize(updatedMetadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, updatedJson, cancellationToken);
    }
}
