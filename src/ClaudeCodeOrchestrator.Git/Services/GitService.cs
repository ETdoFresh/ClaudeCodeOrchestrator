using ClaudeCodeOrchestrator.Git.Models;
using LibGit2Sharp;

namespace ClaudeCodeOrchestrator.Git.Services;

/// <summary>
/// Git service implementation using LibGit2Sharp.
/// </summary>
public sealed class GitService : IGitService
{
    private const string WorktreesDirectoryName = ".worktrees";

    public Task<RepositoryInfo> OpenRepositoryAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var repoPath = Repository.Discover(path)
                ?? throw new InvalidOperationException($"No Git repository found at {path}");

            using var repo = new Repository(repoPath);

            var workDir = repo.Info.WorkingDirectory?.TrimEnd(Path.DirectorySeparatorChar)
                ?? throw new InvalidOperationException("Repository has no working directory");

            var name = Path.GetFileName(workDir);
            var currentBranch = repo.Head.FriendlyName;
            var defaultBranch = GetDefaultBranch(repo);
            var isWorktree = repo.Info.IsBare == false && File.Exists(Path.Combine(workDir, ".git")) && !Directory.Exists(Path.Combine(workDir, ".git"));

            return new RepositoryInfo
            {
                Path = workDir,
                Name = name,
                CurrentBranch = currentBranch,
                DefaultBranch = defaultBranch,
                WorktreesDirectory = Path.Combine(workDir, WorktreesDirectoryName),
                IsWorktree = isWorktree
            };
        }, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetBranchesAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            return (IReadOnlyList<string>)repo.Branches
                .Where(b => !b.IsRemote)
                .Select(b => b.FriendlyName)
                .ToList();
        }, cancellationToken);
    }

    public Task<string> GetCurrentBranchAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            return repo.Head.FriendlyName;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<DiffEntry>> GetDiffAsync(
        string repoPath,
        string? fromRef = null,
        string? toRef = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            var fromTree = fromRef != null
                ? repo.Lookup<Commit>(fromRef)?.Tree
                : null;

            var toTree = toRef != null
                ? repo.Lookup<Commit>(toRef)?.Tree
                : repo.Head.Tip?.Tree;

            if (toTree == null)
            {
                return (IReadOnlyList<DiffEntry>)Array.Empty<DiffEntry>();
            }

            using var diff = repo.Diff.Compare<Patch>(fromTree, toTree);
            return ConvertDiff(diff);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<DiffEntry>> GetUncommittedChangesAsync(
        string repoPath,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            using var diff = repo.Diff.Compare<Patch>(repo.Head.Tip?.Tree, DiffTargets.WorkingDirectory | DiffTargets.Index);
            return ConvertDiff(diff);
        }, cancellationToken);
    }

    public async Task EnsureGitIgnoreEntryAsync(
        string repoPath,
        string entry,
        CancellationToken cancellationToken = default)
    {
        var gitignorePath = Path.Combine(repoPath, ".gitignore");
        var lines = new List<string>();

        if (File.Exists(gitignorePath))
        {
            lines.AddRange(await File.ReadAllLinesAsync(gitignorePath, cancellationToken));
        }

        // Check if entry already exists (with or without trailing slash)
        var normalizedEntry = entry.TrimEnd('/');
        var entryExists = lines.Any(line =>
        {
            var normalizedLine = line.Trim().TrimEnd('/');
            return normalizedLine.Equals(normalizedEntry, StringComparison.OrdinalIgnoreCase);
        });

        if (!entryExists)
        {
            // Add entry with trailing slash for directories
            if (!entry.EndsWith('/'))
            {
                entry += "/";
            }

            lines.Add(entry);
            await File.WriteAllLinesAsync(gitignorePath, lines, cancellationToken);
        }
    }

    public Task<int> GetCommitsAheadAsync(
        string repoPath,
        string branch,
        string baseBranch,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            var branchRef = repo.Branches[branch];
            var baseRef = repo.Branches[baseBranch];

            if (branchRef == null || baseRef == null)
            {
                return 0;
            }

            var divergence = repo.ObjectDatabase.CalculateHistoryDivergence(branchRef.Tip, baseRef.Tip);
            return divergence.AheadBy ?? 0;
        }, cancellationToken);
    }

    public Task<bool> BranchExistsAsync(
        string repoPath,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            return repo.Branches[branchName] != null;
        }, cancellationToken);
    }

    public async Task PushAllBranchesAsync(
        string repoPath,
        CancellationToken cancellationToken = default)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = "push --all",
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
            throw new InvalidOperationException($"Failed to push: {error}");
        }
    }

    private static string GetDefaultBranch(Repository repo)
    {
        // Try common default branch names
        var defaultBranchNames = new[] { "main", "master", "develop" };

        foreach (var name in defaultBranchNames)
        {
            if (repo.Branches[name] != null)
            {
                return name;
            }
        }

        // Fall back to HEAD's branch
        return repo.Head.FriendlyName;
    }

    private static IReadOnlyList<DiffEntry> ConvertDiff(Patch diff)
    {
        var entries = new List<DiffEntry>();

        foreach (var change in diff)
        {
            entries.Add(new DiffEntry
            {
                FilePath = change.Path,
                OldPath = change.OldPath != change.Path ? change.OldPath : null,
                ChangeType = ConvertChangeKind(change.Status),
                LinesAdded = change.LinesAdded,
                LinesDeleted = change.LinesDeleted,
                Patch = change.Patch
            });
        }

        return entries;
    }

    private static DiffChangeType ConvertChangeKind(ChangeKind kind)
    {
        return kind switch
        {
            ChangeKind.Added => DiffChangeType.Added,
            ChangeKind.Deleted => DiffChangeType.Deleted,
            ChangeKind.Modified => DiffChangeType.Modified,
            ChangeKind.Renamed => DiffChangeType.Renamed,
            ChangeKind.Copied => DiffChangeType.Copied,
            ChangeKind.TypeChanged => DiffChangeType.TypeChanged,
            _ => DiffChangeType.Unmodified
        };
    }
}
