using System.Collections.ObjectModel;
using ClaudeCodeOrchestrator.App.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeCodeOrchestrator.App.ViewModels.Docking;

/// <summary>
/// Represents a source option in the Explorer dropdown.
/// </summary>
public class ExplorerSource
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
    /// True if this is the local repository copy, false if it's a worktree.
    /// </summary>
    public bool IsLocalCopy { get; init; }

    /// <summary>
    /// The worktree ID if this is a worktree source, null for local copy.
    /// </summary>
    public string? WorktreeId { get; init; }
}

/// <summary>
/// File browser panel view model.
/// </summary>
public partial class FileBrowserViewModel : ToolViewModelBase
{
    [ObservableProperty]
    private string? _rootPath;

    [ObservableProperty]
    private string? _localCopyPath;

    [ObservableProperty]
    private FileItemViewModel? _selectedItem;

    [ObservableProperty]
    private ExplorerSource? _selectedSource;

    public ObservableCollection<FileItemViewModel> Items { get; } = new();

    /// <summary>
    /// Available sources in the dropdown (Local Copy + worktrees).
    /// </summary>
    public ObservableCollection<ExplorerSource> Sources { get; } = new();

    /// <summary>
    /// Callback to invoke when a file is selected.
    /// First parameter is the file path, second parameter is whether it's a preview (single-click).
    /// </summary>
    public Func<string, bool, Task>? OnFileSelected { get; set; }

    public FileBrowserViewModel()
    {
        Id = "FileBrowser";
        Title = "Explorer";
    }

    partial void OnSelectedSourceChanged(ExplorerSource? value)
    {
        if (value != null)
        {
            LoadDirectory(value.Path);
        }
    }

    /// <summary>
    /// Called when a file item is single-clicked (preview).
    /// </summary>
    [RelayCommand]
    private async Task SelectFileAsync(FileItemViewModel item)
    {
        if (item.IsDirectory) return;
        if (OnFileSelected != null)
        {
            await OnFileSelected(item.FullPath, true);
        }
    }

    /// <summary>
    /// Called when a file item is double-clicked (open persistent).
    /// </summary>
    [RelayCommand]
    private async Task OpenFileAsync(FileItemViewModel item)
    {
        if (item.IsDirectory) return;
        if (OnFileSelected != null)
        {
            await OnFileSelected(item.FullPath, false);
        }
    }

    /// <summary>
    /// Clears the file browser, removing all items and root path.
    /// </summary>
    public void ClearDirectory()
    {
        RootPath = null;
        LocalCopyPath = null;
        Items.Clear();
        Sources.Clear();
        SelectedSource = null;
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

        // Add Local Copy as first option (always available)
        var localSource = new ExplorerSource
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
                Sources.Add(new ExplorerSource
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

    public void LoadDirectory(string path)
    {
        RootPath = path;
        Items.Clear();

        if (!Directory.Exists(path)) return;

        try
        {
            // Load directories first
            foreach (var dir in Directory.GetDirectories(path).OrderBy(d => Path.GetFileName(d)))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith('.') || name == "node_modules" || name == "bin" || name == "obj")
                    continue;

                var item = new FileItemViewModel
                {
                    Name = name,
                    FullPath = dir,
                    IsDirectory = true
                };

                Items.Add(item);
            }

            // Then files
            foreach (var file in Directory.GetFiles(path).OrderBy(f => Path.GetFileName(f)))
            {
                var name = Path.GetFileName(file);

                Items.Add(new FileItemViewModel
                {
                    Name = name,
                    FullPath = file,
                    IsDirectory = false
                });
            }
        }
        catch
        {
            // Ignore errors loading directory
        }
    }

    private void LoadChildren(FileItemViewModel parent)
    {
        if (!parent.IsDirectory) return;

        try
        {
            // Load directories first
            foreach (var dir in Directory.GetDirectories(parent.FullPath).OrderBy(d => Path.GetFileName(d)))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith('.') || name == "node_modules" || name == "bin" || name == "obj")
                    continue;

                var item = new FileItemViewModel
                {
                    Name = name,
                    FullPath = dir,
                    IsDirectory = true
                };

                parent.Children.Add(item);
            }

            // Then files
            foreach (var file in Directory.GetFiles(parent.FullPath).OrderBy(f => Path.GetFileName(f)))
            {
                var name = Path.GetFileName(file);

                parent.Children.Add(new FileItemViewModel
                {
                    Name = name,
                    FullPath = file,
                    IsDirectory = false
                });
            }
        }
        catch
        {
            // Ignore errors
        }
    }

    [RelayCommand]
    private void ExpandItem(FileItemViewModel item)
    {
        if (item.IsDirectory && item.Children.Count == 0)
        {
            LoadChildren(item);
        }
    }

    /// <summary>
    /// Selects a file in the tree by its full path, expanding parent directories as needed.
    /// </summary>
    /// <param name="filePath">The full path of the file to select.</param>
    /// <param name="suppressCallback">If true, won't trigger OnFileSelected callback.</param>
    public void SelectFileByPath(string filePath, bool suppressCallback = true)
    {
        if (string.IsNullOrEmpty(filePath) || Items.Count == 0) return;

        FileItemViewModel? item = null;
        foreach (var rootItem in Items)
        {
            item = FindItemByPath(rootItem, filePath);
            if (item != null) break;
        }

        if (item != null)
        {
            // Temporarily disable callback if requested
            var originalCallback = suppressCallback ? OnFileSelected : null;
            if (suppressCallback) OnFileSelected = null;

            SelectedItem = item;

            if (suppressCallback) OnFileSelected = originalCallback;
        }
    }

    private FileItemViewModel? FindItemByPath(FileItemViewModel parent, string targetPath)
    {
        if (parent.FullPath == targetPath)
            return parent;

        // Check if target is under this parent
        if (!targetPath.StartsWith(parent.FullPath + Path.DirectorySeparatorChar) &&
            !targetPath.StartsWith(parent.FullPath + "/"))
            return null;

        // Expand and load children if needed
        if (parent.IsDirectory && parent.Children.Count == 0)
        {
            LoadChildren(parent);
        }
        parent.IsExpanded = true;

        foreach (var child in parent.Children)
        {
            var found = FindItemByPath(child, targetPath);
            if (found != null)
                return found;
        }

        return null;
    }
}

/// <summary>
/// View model for a file or folder item.
/// </summary>
public partial class FileItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    private bool _isDirectory;

    [ObservableProperty]
    private bool _isExpanded;

    public ObservableCollection<FileItemViewModel> Children { get; } = new();

    public string Icon => IsDirectory ? "📁" : GetFileIcon();

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
