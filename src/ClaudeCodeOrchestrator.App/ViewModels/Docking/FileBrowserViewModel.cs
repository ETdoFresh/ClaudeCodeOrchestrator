using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeCodeOrchestrator.App.ViewModels.Docking;

/// <summary>
/// File browser panel view model.
/// </summary>
public partial class FileBrowserViewModel : ToolViewModelBase
{
    [ObservableProperty]
    private string? _rootPath;

    [ObservableProperty]
    private FileItemViewModel? _selectedItem;

    public ObservableCollection<FileItemViewModel> Items { get; } = new();

    public FileBrowserViewModel()
    {
        Id = "FileBrowser";
        Title = "Explorer";
    }

    public void LoadDirectory(string path)
    {
        RootPath = path;
        Items.Clear();

        if (!Directory.Exists(path)) return;

        try
        {
            var rootItem = new FileItemViewModel
            {
                Name = Path.GetFileName(path),
                FullPath = path,
                IsDirectory = true,
                IsExpanded = true
            };

            LoadChildren(rootItem);
            Items.Add(rootItem);
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
