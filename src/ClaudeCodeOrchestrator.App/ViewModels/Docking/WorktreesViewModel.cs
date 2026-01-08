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
    /// Callback to invoke when a worktree is selected (clicked).
    /// This opens the session for the worktree.
    /// </summary>
    public Func<WorktreeViewModel, Task>? OnWorktreeSelected { get; set; }

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

    [RelayCommand]
    private async Task SelectWorktreeAsync(WorktreeViewModel worktree)
    {
        SelectedWorktree = worktree;
        if (OnWorktreeSelected != null)
            await OnWorktreeSelected(worktree);
    }
}
