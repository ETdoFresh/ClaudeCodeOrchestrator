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

    public async Task<int> GetCommitsAheadOfRemoteAsync(
        string repoPath,
        string branch,
        CancellationToken cancellationToken = default)
    {
        // Use git rev-list to count commits ahead of the remote tracking branch
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"rev-list --count origin/{branch}..{branch}",
            WorkingDirectory = repoPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null)
            return 0;

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            // Remote tracking branch might not exist (new branch not pushed yet)
            // In this case, count all commits from the branch tip to root
            // But for a more practical approach, let's just return 0 if no remote exists
            return 0;
        }

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        return int.TryParse(output.Trim(), out var count) ? count : 0;
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

    public Task<IReadOnlyList<DiffEntry>> GetDiffEntriesBetweenPathsAsync(
        string basePath,
        string comparePath,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var entries = new List<DiffEntry>();

            // Get all files from both directories (excluding common ignored patterns)
            var baseFiles = GetAllFiles(basePath).ToHashSet();
            var compareFiles = GetAllFiles(comparePath).ToHashSet();

            // Find all unique relative paths
            var allPaths = baseFiles.Union(compareFiles).OrderBy(p => p);

            foreach (var relativePath in allPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var baseFilePath = Path.Combine(basePath, relativePath);
                var compareFilePath = Path.Combine(comparePath, relativePath);

                var baseExists = baseFiles.Contains(relativePath);
                var compareExists = compareFiles.Contains(relativePath);

                if (baseExists && compareExists)
                {
                    // Both files exist - check if they're different
                    if (!FilesAreEqual(baseFilePath, compareFilePath))
                    {
                        var (added, deleted) = CountLineDiffs(baseFilePath, compareFilePath);
                        entries.Add(new DiffEntry
                        {
                            FilePath = relativePath,
                            ChangeType = DiffChangeType.Modified,
                            LinesAdded = added,
                            LinesDeleted = deleted
                        });
                    }
                }
                else if (baseExists && !compareExists)
                {
                    // File exists in base but not in compare - deleted in compare
                    var lineCount = CountLines(baseFilePath);
                    entries.Add(new DiffEntry
                    {
                        FilePath = relativePath,
                        ChangeType = DiffChangeType.Deleted,
                        LinesDeleted = lineCount
                    });
                }
                else if (!baseExists && compareExists)
                {
                    // File exists in compare but not in base - added in compare
                    var lineCount = CountLines(compareFilePath);
                    entries.Add(new DiffEntry
                    {
                        FilePath = relativePath,
                        ChangeType = DiffChangeType.Added,
                        LinesAdded = lineCount
                    });
                }
            }

            return (IReadOnlyList<DiffEntry>)entries;
        }, cancellationToken);
    }

    private static IEnumerable<string> GetAllFiles(string rootPath)
    {
        if (!Directory.Exists(rootPath))
            yield break;

        var ignoredDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "node_modules", "bin", "obj", ".vs", ".idea",
            "packages", "dist", "build", ".worktrees"
        };

        var ignoredExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".dll", ".exe", ".pdb", ".cache", ".suo", ".user"
        };

        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            var dirName = Path.GetFileName(dir);

            // Skip ignored directories
            if (ignoredDirs.Contains(dirName) || dirName.StartsWith('.'))
                continue;

            string[] files;
            string[] subdirs;

            try
            {
                files = Directory.GetFiles(dir);
                subdirs = Directory.GetDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                if (ignoredExtensions.Contains(ext))
                    continue;

                var relativePath = Path.GetRelativePath(rootPath, file)
                    .Replace(Path.DirectorySeparatorChar, '/');
                yield return relativePath;
            }

            foreach (var subdir in subdirs)
            {
                stack.Push(subdir);
            }
        }
    }

    private static bool FilesAreEqual(string path1, string path2)
    {
        try
        {
            var info1 = new FileInfo(path1);
            var info2 = new FileInfo(path2);

            // Quick check: different sizes means different content
            if (info1.Length != info2.Length)
                return false;

            // For small files, compare content directly
            if (info1.Length < 1024 * 1024) // 1MB
            {
                return File.ReadAllBytes(path1).SequenceEqual(File.ReadAllBytes(path2));
            }

            // For larger files, compare in chunks
            using var fs1 = File.OpenRead(path1);
            using var fs2 = File.OpenRead(path2);
            var buffer1 = new byte[64 * 1024];
            var buffer2 = new byte[64 * 1024];

            int bytesRead1, bytesRead2;
            while ((bytesRead1 = fs1.Read(buffer1, 0, buffer1.Length)) > 0)
            {
                bytesRead2 = fs2.Read(buffer2, 0, buffer2.Length);
                if (bytesRead1 != bytesRead2)
                    return false;

                for (int i = 0; i < bytesRead1; i++)
                {
                    if (buffer1[i] != buffer2[i])
                        return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int CountLines(string filePath)
    {
        try
        {
            return File.ReadAllLines(filePath).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static (int Added, int Deleted) CountLineDiffs(string basePath, string comparePath)
    {
        try
        {
            var baseLines = File.ReadAllLines(basePath);
            var compareLines = File.ReadAllLines(comparePath);

            // Simple approximation: count lines that differ
            var baseSet = new HashSet<string>(baseLines);
            var compareSet = new HashSet<string>(compareLines);

            var added = compareLines.Count(line => !baseSet.Contains(line));
            var deleted = baseLines.Count(line => !compareSet.Contains(line));

            return (added, deleted);
        }
        catch
        {
            return (0, 0);
        }
    }
}
