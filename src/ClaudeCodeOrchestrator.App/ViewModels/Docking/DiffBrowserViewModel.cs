using System.Collections.ObjectModel;
using ClaudeCodeOrchestrator.App.ViewModels;
using ClaudeCodeOrchestrator.Git.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeCodeOrchestrator.App.ViewModels.Docking;

/// <summary>
/// Represents a source option in the Diff browser dropdown.
/// </summary>
public class DiffSource
{
    /// <summary>
    /// Display name shown in the dropdown.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// The path this source points to.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// True if this is the local repository copy (shows no diff), false if it's a worktree.
    /// </summary>
    public bool IsLocalCopy { get; init; }

    /// <summary>
    /// The worktree ID if this is a worktree source, null for local copy.
    /// </summary>
    public string? WorktreeId { get; init; }
}

/// <summary>
/// Diff browser panel view model - shows file differences between local copy and worktrees.
/// </summary>
public partial class DiffBrowserViewModel : ToolViewModelBase
{
    [ObservableProperty]
    private string? _localCopyPath;

    [ObservableProperty]
    private DiffFileItemViewModel? _selectedItem;

    [ObservableProperty]
    private DiffSource? _selectedSource;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    public ObservableCollection<DiffFileItemViewModel> Items { get; } = new();

    /// <summary>
    /// Available sources in the dropdown (Local Copy + worktrees).
    /// </summary>
    public ObservableCollection<DiffSource> Sources { get; } = new();

    /// <summary>
    /// Callback to invoke when a diff file is selected.
    /// Parameters: localPath, worktreePath, relativePath, isPreview
    /// </summary>
    public Func<string, string, string, bool, Task>? OnDiffFileSelected { get; set; }

    /// <summary>
    /// Callback to load diff entries for a worktree.
    /// Parameters: localPath, worktreePath
    /// Returns: list of diff entries
    /// </summary>
    public Func<string, string, Task<IReadOnlyList<DiffEntry>>>? OnLoadDiffEntries { get; set; }

    public DiffBrowserViewModel()
    {
        Id = "DiffBrowser";
        Title = "Diff";
    }

    partial void OnSelectedSourceChanged(DiffSource? value)
    {
        if (value != null)
        {
            _ = LoadDiffAsync(value);
        }
    }

    /// <summary>
    /// Called when a diff file item is single-clicked (preview).
    /// </summary>
    [RelayCommand]
    private async Task SelectDiffFileAsync(DiffFileItemViewModel item)
    {
        if (item.IsDirectory) return;
        if (OnDiffFileSelected != null && SelectedSource != null && LocalCopyPath != null)
        {
            await OnDiffFileSelected(LocalCopyPath, SelectedSource.Path, item.RelativePath, true);
        }
    }

    /// <summary>
    /// Called when a diff file item is double-clicked (open persistent).
    /// </summary>
    [RelayCommand]
    private async Task OpenDiffFileAsync(DiffFileItemViewModel item)
    {
        if (item.IsDirectory) return;
        if (OnDiffFileSelected != null && SelectedSource != null && LocalCopyPath != null)
        {
            await OnDiffFileSelected(LocalCopyPath, SelectedSource.Path, item.RelativePath, false);
        }
    }

    /// <summary>
    /// Clears the diff browser, removing all items.
    /// </summary>
    public void ClearDiff()
    {
        LocalCopyPath = null;
        Items.Clear();
        Sources.Clear();
        SelectedSource = null;
        StatusMessage = null;
    }

    /// <summary>
    /// Updates the sources list with Local Copy and available worktrees.
    /// </summary>
    /// <param name="localPath">The main repository path.</param>
    /// <param name="worktrees">The list of available worktrees.</param>
    public void UpdateSources(string localPath, IEnumerable<WorktreeViewModel>? worktrees)
    {
        LocalCopyPath = localPath;
        var currentSelectedPath = SelectedSource?.Path;

        Sources.Clear();

        // Add Local Copy as first option (shows no diff when selected)
        var localSource = new DiffSource
        {
            DisplayName = "Local Copy",
            Path = localPath,
            IsLocalCopy = true,
            WorktreeId = null
        };
        Sources.Add(localSource);

        // Add worktrees
        if (worktrees != null)
        {
            foreach (var wt in worktrees)
            {
                Sources.Add(new DiffSource
                {
                    DisplayName = wt.DisplayTitle,
                    Path = wt.Path,
                    IsLocalCopy = false,
                    WorktreeId = wt.Id
                });
            }
        }

        // Restore selection or default to Local Copy
        var previousSource = currentSelectedPath != null
            ? Sources.FirstOrDefault(s => s.Path == currentSelectedPath)
            : null;

        SelectedSource = previousSource ?? localSource;
    }

    private async Task LoadDiffAsync(DiffSource source)
    {
        Items.Clear();

        if (source.IsLocalCopy)
        {
            // Local copy selected - show message that there's no diff
            StatusMessage = "Select a worktree to view differences from local copy.";
            return;
        }

        StatusMessage = null;

        if (OnLoadDiffEntries == null || LocalCopyPath == null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var diffEntries = await OnLoadDiffEntries(LocalCopyPath, source.Path);
            BuildDiffTree(diffEntries);

            if (Items.Count == 0)
            {
                StatusMessage = "No differences found.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading diff: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void BuildDiffTree(IReadOnlyList<DiffEntry> entries)
    {
        // Build a tree structure from flat diff entries, excluding identical files
        var rootItems = new Dictionary<string, DiffFileItemViewModel>();

        foreach (var entry in entries.Where(e => e.ChangeType != DiffChangeType.Unmodified))
        {
            var parts = entry.FilePath.Split('/', '\\');
            DiffFileItemViewModel? parent = null;
            var currentPath = "";

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var isLastPart = i == parts.Length - 1;
                currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";

                DiffFileItemViewModel? node;

                if (parent == null)
                {
                    // Root level
                    if (!rootItems.TryGetValue(currentPath, out node))
                    {
                        node = new DiffFileItemViewModel
                        {
                            Name = part,
                            RelativePath = currentPath,
                            IsDirectory = !isLastPart,
                            ChangeType = isLastPart ? entry.ChangeType : null,
                            LinesAdded = isLastPart ? entry.LinesAdded : 0,
                            LinesDeleted = isLastPart ? entry.LinesDeleted : 0
                        };
                        rootItems[currentPath] = node;
                        Items.Add(node);
                    }
                }
                else
                {
                    // Child level
                    node = parent.Children.FirstOrDefault(c => c.Name == part);
                    if (node == null)
                    {
                        node = new DiffFileItemViewModel
                        {
                            Name = part,
                            RelativePath = currentPath,
                            IsDirectory = !isLastPart,
                            ChangeType = isLastPart ? entry.ChangeType : null,
                            LinesAdded = isLastPart ? entry.LinesAdded : 0,
                            LinesDeleted = isLastPart ? entry.LinesDeleted : 0
                        };
                        parent.Children.Add(node);
                    }
                }

                parent = node;
            }
        }

        // Sort items: directories first, then alphabetically
        SortItems(Items);
    }

    private void SortItems(ObservableCollection<DiffFileItemViewModel> items)
    {
        var sorted = items.OrderByDescending(i => i.IsDirectory).ThenBy(i => i.Name).ToList();
        items.Clear();
        foreach (var item in sorted)
        {
            items.Add(item);
            if (item.IsDirectory)
            {
                SortItems(item.Children);
            }
        }
    }

    [RelayCommand]
    private void ExpandItem(DiffFileItemViewModel item)
    {
        // Just toggle expansion - children are already loaded
        item.IsExpanded = !item.IsExpanded;
    }
}

/// <summary>
/// View model for a diff file or folder item.
/// </summary>
public partial class DiffFileItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _relativePath = string.Empty;

    [ObservableProperty]
    private bool _isDirectory;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private DiffChangeType? _changeType;

    [ObservableProperty]
    private int _linesAdded;

    [ObservableProperty]
    private int _linesDeleted;

    public ObservableCollection<DiffFileItemViewModel> Children { get; } = new();

    public string Icon => IsDirectory ? "📁" : GetFileIcon();

    public string ChangeIcon => ChangeType switch
    {
        DiffChangeType.Added => "➕",
        DiffChangeType.Deleted => "➖",
        DiffChangeType.Modified => "✏️",
        DiffChangeType.Renamed => "📝",
        DiffChangeType.Copied => "📋",
        _ => ""
    };

    public string ChangeStats => (LinesAdded > 0 || LinesDeleted > 0)
        ? $"+{LinesAdded} -{LinesDeleted}"
        : "";

    private string GetFileIcon()
    {
        var ext = Path.GetExtension(Name).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "🔷",
            ".axaml" or ".xaml" => "🎨",
            ".json" => "📋",
            ".md" => "📝",
            ".csproj" or ".sln" => "⚙️",
            ".gitignore" => "🚫",
            _ => "📄"
        };
    }
}
