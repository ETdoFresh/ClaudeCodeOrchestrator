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

    public WorktreeService(IGitService gitService)
    {
        _gitService = gitService;
    }

    public async Task<WorktreeInfo> CreateWorktreeAsync(
        string repoPath,
        string taskDescription,
        string? title = null,
        string? branchName = null,
        string? baseBranch = null,
        string? taskBranchPrefix = null,
        string? jobBranchPrefix = null,
        CancellationToken cancellationToken = default)
    {
        var repoInfo = await _gitService.OpenRepositoryAsync(repoPath, cancellationToken);
        baseBranch ??= repoInfo.DefaultBranch;

        // Create a BranchNameGenerator with the specified prefixes (or defaults)
        var generator = new BranchNameGenerator(
            taskBranchPrefix ?? BranchNameGenerator.DefaultTaskPrefix,
            jobBranchPrefix ?? BranchNameGenerator.DefaultJobPrefix);

        // Use provided branch name with timestamp, or generate from task description
        var finalBranchName = branchName != null
            ? generator.AddTimestamp(branchName)
            : generator.Generate(taskDescription);

        // Ensure worktrees directory exists and is gitignored
        var worktreesDir = Path.Combine(repoPath, WorktreesDirectoryName);
        Directory.CreateDirectory(worktreesDir);
        await _gitService.EnsureGitIgnoreEntriesAsync(repoPath, [WorktreesDirectoryName, MetadataFileName], cancellationToken);

        // Create worktree path (strip branch prefixes for directory name)
        var worktreeId = Guid.NewGuid().ToString("N")[..8];
        // Strip any prefix that ends with / to get a cleaner directory name
        var pathSafeBranchName = finalBranchName;
        var slashIndex = pathSafeBranchName.IndexOf('/');
        if (slashIndex >= 0)
        {
            pathSafeBranchName = pathSafeBranchName[(slashIndex + 1)..];
        }
        var worktreePath = Path.Combine(worktreesDir, $"{pathSafeBranchName}-{worktreeId}");

        // Create worktree using git command (LibGit2Sharp worktree support is limited)
        await CreateWorktreeViaGitAsync(repoPath, finalBranchName, worktreePath, baseBranch, cancellationToken);

        // Save metadata
        var metadata = new WorktreeMetadata
        {
            Id = worktreeId,
            TaskDescription = taskDescription,
            Title = title,
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
            BranchName = finalBranchName,
            BaseBranch = baseBranch,
            TaskDescription = taskDescription,
            Title = title,
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

        var worktreePath = worktree.Path;

        // First, try to kill any processes that might be locking files in the worktree
        await KillProcessesInDirectoryAsync(worktreePath);

        // Small delay to let processes terminate
        await Task.Delay(200, cancellationToken);

        // Remove worktree using git command
        await RemoveWorktreeViaGitAsync(repoPath, worktreePath, force, cancellationToken);

        // Delete the directory if it still exists, with retry logic for Windows file locking
        if (Directory.Exists(worktreePath))
        {
            await DeleteDirectoryWithRetryAsync(worktreePath, cancellationToken);
        }

        // If directory still exists after all attempts, try one more time after killing processes again
        if (Directory.Exists(worktreePath))
        {
            await KillProcessesInDirectoryAsync(worktreePath);
            await Task.Delay(500, cancellationToken);
            await ForceDeleteDirectoryAsync(worktreePath);
        }

        // Optionally delete the branch
        // For now, we'll keep the branch in case user wants to reference it
    }

    /// <summary>
    /// Kills any processes that have files open in the specified directory.
    /// </summary>
    private static async Task KillProcessesInDirectoryAsync(string directoryPath)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            // Use handle.exe or PowerShell to find processes with handles in this directory
            // Fallback: just try to find common culprits like git, node, etc.
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -Command \"Get-Process | Where-Object {{ $_.Path -like '{directoryPath.Replace("\\", "\\\\")}*' }} | Stop-Process -Force -ErrorAction SilentlyContinue\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
            }
        }
        catch
        {
            // Ignore errors - this is best effort
        }

        // Also try to find processes with working directory in the path
        try
        {
            var normalizedPath = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
            foreach (var proc in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    // Check if process has main module in the directory
                    if (proc.MainModule?.FileName?.ToLowerInvariant().StartsWith(normalizedPath) == true)
                    {
                        proc.Kill();
                    }
                }
                catch
                {
                    // Ignore - we can't access some processes
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
            // Ignore errors
        }
    }

    /// <summary>
    /// Force deletes a directory using cmd /c rd command which can sometimes succeed where Directory.Delete fails.
    /// </summary>
    private static async Task ForceDeleteDirectoryAsync(string path)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            // Clear read-only attributes first
            ClearReadOnlyAttributes(path);

            // Try using cmd /c rd /s /q which is more aggressive on Windows
            if (OperatingSystem.IsWindows())
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c rd /s /q \"{path}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                }
            }
            else
            {
                // On Unix, use rm -rf
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "rm",
                    Arguments = $"-rf \"{path}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                }
            }
        }
        catch
        {
            // Ignore - this was a last resort
        }

        // Final attempt with .NET
        if (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Give up silently
            }
        }
    }

    /// <summary>
    /// Deletes a directory with retry logic to handle Windows file locking issues.
    /// </summary>
    private static async Task DeleteDirectoryWithRetryAsync(
        string path,
        CancellationToken cancellationToken,
        int maxRetries = 5)
    {
        var delays = new[] { 100, 200, 500, 1000, 2000 }; // Exponential backoff in ms

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Force garbage collection to release any managed file handles
                if (attempt > 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }

                // Clear read-only attributes that might prevent deletion
                ClearReadOnlyAttributes(path);

                Directory.Delete(path, recursive: true);
                return; // Success
            }
            catch (IOException) when (attempt < maxRetries)
            {
                // File is locked, wait and retry
                await Task.Delay(delays[Math.Min(attempt, delays.Length - 1)], cancellationToken);
            }
            catch (UnauthorizedAccessException) when (attempt < maxRetries)
            {
                // Access denied (often due to file locks on Windows), wait and retry
                await Task.Delay(delays[Math.Min(attempt, delays.Length - 1)], cancellationToken);
            }
            catch (IOException)
            {
                // Final attempt failed, try aggressive cleanup
                await AggressiveDeleteAsync(path, cancellationToken);
                return;
            }
            catch (UnauthorizedAccessException)
            {
                // Final attempt failed, try aggressive cleanup
                await AggressiveDeleteAsync(path, cancellationToken);
                return;
            }
        }
    }

    /// <summary>
    /// Aggressively deletes a directory by deleting files first, then empty directories bottom-up.
    /// </summary>
    private static async Task AggressiveDeleteAsync(string path, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path))
            return;

        // First, delete all files we can
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch
            {
                // Ignore files we can't delete
            }
        }

        // Small delay to let file handles release
        await Task.Delay(100, cancellationToken);

        // Delete directories bottom-up (deepest first)
        var directories = Directory.GetDirectories(path, "*", SearchOption.AllDirectories)
            .OrderByDescending(d => d.Length) // Longer paths = deeper directories
            .ToList();

        foreach (var dir in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir, false);
                }
            }
            catch
            {
                // Ignore directories we can't delete
            }
        }

        // Finally try to delete the root directory
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path, false);
            }
        }
        catch
        {
            // If we still can't delete the root, give up silently
            // The directory should at least be empty now
        }
    }

    /// <summary>
    /// Recursively clears read-only attributes from all files in a directory.
    /// </summary>
    private static void ClearReadOnlyAttributes(string path)
    {
        try
        {
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var attributes = File.GetAttributes(file);
                    if ((attributes & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                    }
                }
                catch
                {
                    // Ignore individual file errors
                }
            }
        }
        catch
        {
            // Ignore errors when enumerating
        }
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

            // Get commits ahead of base branch (for merge readiness)
            var commitsAhead = await _gitService.GetCommitsAheadAsync(
                worktreePath, branchName, metadata.BaseBranch, cancellationToken);

            // Get unpushed commits (for push badge)
            var unpushedCommits = await _gitService.GetCommitsAheadOfRemoteAsync(
                worktreePath, branchName, cancellationToken);

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
                Title = metadata.Title,
                CreatedAt = metadata.CreatedAt,
                Status = status,
                HasUncommittedChanges = hasUncommittedChanges,
                CommitsAhead = commitsAhead,
                UnpushedCommits = unpushedCommits,
                ClaudeSessionId = metadata.ClaudeSessionId,
                SessionWasActive = metadata.SessionWasActive,
                AccumulatedDurationMs = metadata.AccumulatedDurationMs,
                AccumulatedCostUsd = metadata.AccumulatedCostUsd,
                WasJob = metadata.WasJob,
                LastIteration = metadata.LastIteration,
                JobMaxIterations = metadata.MaxIterations
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
        // First, abort any existing merge that might be in progress
        // This ensures the repo is in a clean state before we start
        await AbortMergeAsync(repoPath, cancellationToken);

        // Check for uncommitted changes that would block checkout
        var hasUncommittedChanges = await HasUncommittedChangesAsync(repoPath, cancellationToken);
        if (hasUncommittedChanges)
        {
            return new MergeResultModel
            {
                Success = false,
                Status = MergeStatusModel.Failed,
                ErrorMessage = "Cannot merge: you have uncommitted changes. Please commit or stash them first."
            };
        }

        // Checkout target branch
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

    private static async Task<bool> HasUncommittedChangesAsync(string repoPath, CancellationToken cancellationToken)
    {
        var statusPsi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = "status --porcelain",
            WorkingDirectory = repoPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var statusProcess = System.Diagnostics.Process.Start(statusPsi);
        if (statusProcess != null)
        {
            await statusProcess.WaitForExitAsync(cancellationToken);
            var output = await statusProcess.StandardOutput.ReadToEndAsync(cancellationToken);
            // If output is not empty, there are uncommitted changes
            return !string.IsNullOrWhiteSpace(output);
        }
        return false;
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
        public string? Title { get; init; }
        public required string BaseBranch { get; init; }
        public required DateTime CreatedAt { get; init; }
        public string? ClaudeSessionId { get; init; }
        public bool SessionWasActive { get; init; }
        public long AccumulatedDurationMs { get; init; }
        public decimal AccumulatedCostUsd { get; init; }

        // Job metadata for persistence across app restarts
        public bool WasJob { get; init; }
        public int? LastIteration { get; init; }
        public int? MaxIterations { get; init; }
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

    /// <summary>
    /// Updates the SessionWasActive flag for a worktree.
    /// </summary>
    public async Task UpdateSessionWasActiveAsync(
        string worktreePath,
        bool wasActive,
        CancellationToken cancellationToken = default)
    {
        var metadataPath = Path.Combine(worktreePath, MetadataFileName);
        if (!File.Exists(metadataPath))
            return;

        var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
        var metadata = JsonSerializer.Deserialize<WorktreeMetadata>(json);
        if (metadata == null)
            return;

        var updatedMetadata = metadata with { SessionWasActive = wasActive };
        var updatedJson = JsonSerializer.Serialize(updatedMetadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, updatedJson, cancellationToken);
    }

    /// <summary>
    /// Updates the accumulated duration for a worktree session.
    /// </summary>
    public async Task UpdateAccumulatedDurationAsync(
        string worktreePath,
        long durationMs,
        CancellationToken cancellationToken = default)
    {
        var metadataPath = Path.Combine(worktreePath, MetadataFileName);
        if (!File.Exists(metadataPath))
            return;

        var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
        var metadata = JsonSerializer.Deserialize<WorktreeMetadata>(json);
        if (metadata == null)
            return;

        var updatedMetadata = metadata with { AccumulatedDurationMs = durationMs };
        var updatedJson = JsonSerializer.Serialize(updatedMetadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, updatedJson, cancellationToken);
    }

    /// <summary>
    /// Updates the accumulated cost for a worktree session.
    /// </summary>
    public async Task UpdateAccumulatedCostAsync(
        string worktreePath,
        decimal costUsd,
        CancellationToken cancellationToken = default)
    {
        var metadataPath = Path.Combine(worktreePath, MetadataFileName);
        if (!File.Exists(metadataPath))
            return;

        var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
        var metadata = JsonSerializer.Deserialize<WorktreeMetadata>(json);
        if (metadata == null)
            return;

        var updatedMetadata = metadata with { AccumulatedCostUsd = costUsd };
        var updatedJson = JsonSerializer.Serialize(updatedMetadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, updatedJson, cancellationToken);
    }

    /// <summary>
    /// Updates the title for a worktree.
    /// </summary>
    public async Task UpdateTitleAsync(
        string worktreePath,
        string title,
        CancellationToken cancellationToken = default)
    {
        var metadataPath = Path.Combine(worktreePath, MetadataFileName);
        if (!File.Exists(metadataPath))
            return;

        var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
        var metadata = JsonSerializer.Deserialize<WorktreeMetadata>(json);
        if (metadata == null)
            return;

        var updatedMetadata = metadata with { Title = title };
        var updatedJson = JsonSerializer.Serialize(updatedMetadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, updatedJson, cancellationToken);
    }

    /// <summary>
    /// Updates the job metadata for a worktree (for persistence across app restarts).
    /// </summary>
    public async Task UpdateJobMetadataAsync(
        string worktreePath,
        bool wasJob,
        int? lastIteration,
        int? maxIterations,
        CancellationToken cancellationToken = default)
    {
        var metadataPath = Path.Combine(worktreePath, MetadataFileName);
        if (!File.Exists(metadataPath))
            return;

        var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
        var metadata = JsonSerializer.Deserialize<WorktreeMetadata>(json);
        if (metadata == null)
            return;

        var updatedMetadata = metadata with
        {
            WasJob = wasJob,
            LastIteration = lastIteration,
            MaxIterations = maxIterations
        };
        var updatedJson = JsonSerializer.Serialize(updatedMetadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, updatedJson, cancellationToken);
    }
}
