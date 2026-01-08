using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeCodeOrchestrator.App.ViewModels;

/// <summary>
/// Main window view model.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private object? _layout;
    private string? _currentRepositoryPath;
    private string _windowTitle = "Claude Code Orchestrator";
    private bool _isRepositoryOpen;

    /// <summary>
    /// Gets or sets the dock layout (IRootDock).
    /// Using object type to avoid source generator issues with external types.
    /// </summary>
    public object? Layout
    {
        get => _layout;
        set => SetProperty(ref _layout, value);
    }

    public string? CurrentRepositoryPath
    {
        get => _currentRepositoryPath;
        set => SetProperty(ref _currentRepositoryPath, value);
    }

    public string WindowTitle
    {
        get => _windowTitle;
        set => SetProperty(ref _windowTitle, value);
    }

    public bool IsRepositoryOpen
    {
        get => _isRepositoryOpen;
        set => SetProperty(ref _isRepositoryOpen, value);
    }

    public ObservableCollection<SessionViewModel> Sessions { get; } = new();

    public ObservableCollection<WorktreeViewModel> Worktrees { get; } = new();

    [RelayCommand]
    private async Task OpenRepositoryAsync()
    {
        // Will be implemented with folder picker
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task CreateTaskAsync()
    {
        // Will be implemented with new task dialog
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void CloseRepository()
    {
        CurrentRepositoryPath = null;
        IsRepositoryOpen = false;
        WindowTitle = "Claude Code Orchestrator";
        Sessions.Clear();
        Worktrees.Clear();
    }

    public void SetRepository(string path)
    {
        CurrentRepositoryPath = path;
        IsRepositoryOpen = true;
        WindowTitle = $"Claude Code Orchestrator - {System.IO.Path.GetFileName(path)}";
    }
}
