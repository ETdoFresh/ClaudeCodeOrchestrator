using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeCodeOrchestrator.App.ViewModels.Docking;

/// <summary>
/// Represents a line in a diff view.
/// </summary>
public class DiffLine
{
    /// <summary>
    /// The line number in the old file (null if line was added).
    /// </summary>
    public int? OldLineNumber { get; init; }

    /// <summary>
    /// The line number in the new file (null if line was deleted).
    /// </summary>
    public int? NewLineNumber { get; init; }

    /// <summary>
    /// The content of the line.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// The type of change for this line.
    /// </summary>
    public DiffLineType Type { get; init; }

    /// <summary>
    /// Background color based on the line type.
    /// </summary>
    public string BackgroundColor => Type switch
    {
        DiffLineType.Added => "#1F3A1F",
        DiffLineType.Deleted => "#3A1F1F",
        DiffLineType.Header => "#2D2D50",
        _ => "Transparent"
    };

    /// <summary>
    /// Foreground color based on the line type.
    /// </summary>
    public string ForegroundColor => Type switch
    {
        DiffLineType.Added => "#4EC94E",
        DiffLineType.Deleted => "#F85149",
        DiffLineType.Header => "#79B8FF",
        _ => "#D4D4D4"
    };

    /// <summary>
    /// Prefix character for the line (+, -, space).
    /// </summary>
    public string Prefix => Type switch
    {
        DiffLineType.Added => "+",
        DiffLineType.Deleted => "-",
        DiffLineType.Header => "",
        _ => " "
    };
}

/// <summary>
/// Type of diff line.
/// </summary>
public enum DiffLineType
{
    Unchanged,
    Added,
    Deleted,
    Header
}

/// <summary>
/// Document view model for viewing file diffs.
/// </summary>
public partial class DiffDocumentViewModel : DocumentViewModelBase
{
    [ObservableProperty]
    private string _relativePath = string.Empty;

    [ObservableProperty]
    private string _localFilePath = string.Empty;

    [ObservableProperty]
    private string _worktreeFilePath = string.Empty;

    [ObservableProperty]
    private bool _isPreview;

    [ObservableProperty]
    private string _localContent = string.Empty;

    [ObservableProperty]
    private string _worktreeContent = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<DiffLine> DiffLines { get; } = new();

    public DiffDocumentViewModel()
    {
        Id = Guid.NewGuid().ToString();
        Title = "Diff";
        CanClose = true;
        CanFloat = true;
    }

    public DiffDocumentViewModel(string localPath, string worktreePath, string relativePath)
    {
        RelativePath = relativePath;
        LocalFilePath = Path.Combine(localPath, relativePath);
        WorktreeFilePath = Path.Combine(worktreePath, relativePath);
        Id = $"diff:{LocalFilePath}:{WorktreeFilePath}";
        Title = $"Diff: {Path.GetFileName(relativePath)}";
        CanClose = true;
        CanFloat = true;

        // Load the diff content
        LoadDiff();
    }

    private void LoadDiff()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            // Read both files
            var localExists = File.Exists(LocalFilePath);
            var worktreeExists = File.Exists(WorktreeFilePath);

            if (!localExists && !worktreeExists)
            {
                ErrorMessage = "Both files do not exist.";
                return;
            }

            LocalContent = localExists ? File.ReadAllText(LocalFilePath) : string.Empty;
            WorktreeContent = worktreeExists ? File.ReadAllText(WorktreeFilePath) : string.Empty;

            // Generate diff
            GenerateDiff();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading diff: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void GenerateDiff()
    {
        DiffLines.Clear();

        var localLines = LocalContent.Split('\n');
        var worktreeLines = WorktreeContent.Split('\n');

        // Add header
        DiffLines.Add(new DiffLine
        {
            Content = $"--- Local: {RelativePath}",
            Type = DiffLineType.Header
        });
        DiffLines.Add(new DiffLine
        {
            Content = $"+++ Worktree: {RelativePath}",
            Type = DiffLineType.Header
        });

        // Use a simple line-by-line diff algorithm
        var diff = ComputeDiff(localLines, worktreeLines);

        int oldLineNum = 1;
        int newLineNum = 1;

        foreach (var (type, content) in diff)
        {
            var line = new DiffLine
            {
                Content = content.TrimEnd('\r'),
                Type = type,
                OldLineNumber = type != DiffLineType.Added ? oldLineNum : null,
                NewLineNumber = type != DiffLineType.Deleted ? newLineNum : null
            };

            DiffLines.Add(line);

            if (type != DiffLineType.Added) oldLineNum++;
            if (type != DiffLineType.Deleted) newLineNum++;
        }
    }

    /// <summary>
    /// Computes a simple diff between two arrays of lines using Myers' algorithm approximation.
    /// </summary>
    private static List<(DiffLineType Type, string Content)> ComputeDiff(string[] oldLines, string[] newLines)
    {
        var result = new List<(DiffLineType, string)>();

        // Use Longest Common Subsequence (LCS) approach for better diff quality
        var lcs = ComputeLCS(oldLines, newLines);

        int oldIdx = 0;
        int newIdx = 0;
        int lcsIdx = 0;

        while (oldIdx < oldLines.Length || newIdx < newLines.Length)
        {
            if (lcsIdx < lcs.Count)
            {
                var (lcsOldIdx, lcsNewIdx) = lcs[lcsIdx];

                // Add deleted lines before the LCS match
                while (oldIdx < lcsOldIdx)
                {
                    result.Add((DiffLineType.Deleted, oldLines[oldIdx]));
                    oldIdx++;
                }

                // Add added lines before the LCS match
                while (newIdx < lcsNewIdx)
                {
                    result.Add((DiffLineType.Added, newLines[newIdx]));
                    newIdx++;
                }

                // Add the matching line
                result.Add((DiffLineType.Unchanged, oldLines[oldIdx]));
                oldIdx++;
                newIdx++;
                lcsIdx++;
            }
            else
            {
                // After all LCS matches, add remaining lines
                while (oldIdx < oldLines.Length)
                {
                    result.Add((DiffLineType.Deleted, oldLines[oldIdx]));
                    oldIdx++;
                }

                while (newIdx < newLines.Length)
                {
                    result.Add((DiffLineType.Added, newLines[newIdx]));
                    newIdx++;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Computes the Longest Common Subsequence indices.
    /// </summary>
    private static List<(int OldIdx, int NewIdx)> ComputeLCS(string[] oldLines, string[] newLines)
    {
        int m = oldLines.Length;
        int n = newLines.Length;

        // Build LCS table
        var dp = new int[m + 1, n + 1];
        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                if (oldLines[i - 1] == newLines[j - 1])
                {
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                }
                else
                {
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }
            }
        }

        // Backtrack to find LCS indices
        var result = new List<(int, int)>();
        int x = m, y = n;
        while (x > 0 && y > 0)
        {
            if (oldLines[x - 1] == newLines[y - 1])
            {
                result.Add((x - 1, y - 1));
                x--;
                y--;
            }
            else if (dp[x - 1, y] > dp[x, y - 1])
            {
                x--;
            }
            else
            {
                y--;
            }
        }

        result.Reverse();
        return result;
    }

    /// <summary>
    /// Reloads the diff from disk.
    /// </summary>
    public void Reload()
    {
        LoadDiff();
    }
}
