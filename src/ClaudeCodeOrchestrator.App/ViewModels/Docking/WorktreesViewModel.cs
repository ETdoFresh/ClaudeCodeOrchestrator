using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeCodeOrchestrator.App.ViewModels.Docking;

/// <summary>
/// Worktrees panel view model.
/// </summary>
public partial class WorktreesViewModel : ToolViewModelBase
{
    [ObservableProperty]
    private WorktreeViewModel? _selectedWorktree;

    public ObservableCollection<WorktreeViewModel> Worktrees { get; } = new();

    /// <summary>
    /// Callback to invoke when the user requests to create a new task.
    /// This is wired up by the DockFactory to call MainWindowViewModel.CreateTaskAsync.
    /// </summary>
    public Func<Task>? OnCreateTaskRequested { get; set; }

    /// <summary>
    /// Callback to invoke when the user requests to refresh worktrees.
    /// This is wired up by the DockFactory to call MainWindowViewModel.RefreshWorktreesAsync.
    /// </summary>
    public Func<Task>? OnRefreshRequested { get; set; }

    /// <summary>
    /// Callback to invoke when a worktree is selected (single-clicked).
    /// First parameter is the worktree, second parameter is whether it's a preview (single-click = true).
    /// </summary>
    public Func<WorktreeViewModel, bool, Task>? OnWorktreeSelected { get; set; }

    public WorktreesViewModel()
    {
        Id = "Worktrees";
        Title = "Worktrees";
    }

    [RelayCommand]
    private async Task CreateTaskAsync()
    {
        if (OnCreateTaskRequested != null)
            await OnCreateTaskRequested();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (OnRefreshRequested != null)
            await OnRefreshRequested();
    }

    /// <summary>
    /// Called when a worktree is single-clicked (preview mode).
    /// </summary>
    [RelayCommand]
    private async Task SelectWorktreeAsync(WorktreeViewModel worktree)
    {
        SelectedWorktree = worktree;
        if (OnWorktreeSelected != null)
            await OnWorktreeSelected(worktree, true); // true = preview
    }

    /// <summary>
    /// Called when a worktree is double-clicked (open persistent).
    /// </summary>
    [RelayCommand]
    private async Task OpenWorktreeAsync(WorktreeViewModel worktree)
    {
        SelectedWorktree = worktree;
        if (OnWorktreeSelected != null)
            await OnWorktreeSelected(worktree, false); // false = not preview
    }
}
