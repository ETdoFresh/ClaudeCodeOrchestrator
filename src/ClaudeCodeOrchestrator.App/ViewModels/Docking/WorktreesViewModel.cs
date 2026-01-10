using System.Collections.ObjectModel;
using ClaudeCodeOrchestrator.App.Models;
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
    /// Unpushed commits for the main repository (not worktrees).
    /// </summary>
    private int _mainRepoUnpushedCommits;

    /// <summary>
    /// Whether the repository has a remote configured.
    /// </summary>
    private bool _hasRemote;

    /// <summary>
    /// Gets the number of unpushed commits in the main repository.
    /// This is what gets displayed in the Push button badge.
    /// </summary>
    public int TotalCommitsToPush => _mainRepoUnpushedCommits;

    /// <summary>
    /// Gets whether the repository has a remote configured.
    /// Used to show/hide the Push button.
    /// </summary>
    public bool HasRemote => _hasRemote;

    /// <summary>
    /// Sets the unpushed commits count for the main repository.
    /// </summary>
    public void SetMainRepoUnpushedCommits(int count)
    {
        if (_mainRepoUnpushedCommits == count) return;
        _mainRepoUnpushedCommits = count;
        OnPropertyChanged(nameof(TotalCommitsToPush));
    }

    /// <summary>
    /// Sets whether the repository has a remote configured.
    /// </summary>
    public void SetHasRemote(bool hasRemote)
    {
        if (_hasRemote == hasRemote) return;
        _hasRemote = hasRemote;
        OnPropertyChanged(nameof(HasRemote));
    }

    /// <summary>
    /// Callback to invoke when the user requests to create a new task (legacy, from button).
    /// This is wired up by the DockFactory to call MainWindowViewModel.CreateTaskAsync.
    /// </summary>
    public Func<Task>? OnCreateTaskRequested { get; set; }

    /// <summary>
    /// Callback to invoke when the user requests to create a new task with input (from inline control).
    /// The TaskInput contains the description and images, title/branch will be generated.
    /// </summary>
    public Func<TaskInput, Task>? OnCreateTaskWithInputRequested { get; set; }

    /// <summary>
    /// Callback to invoke when the user requests to refresh worktrees.
    /// This is wired up by the DockFactory to call MainWindowViewModel.RefreshWorktreesAsync.
    /// </summary>
    public Func<Task>? OnRefreshRequested { get; set; }

    /// <summary>
    /// Callback to invoke when the user requests to push all branches.
    /// This is wired up by the DockFactory to call MainWindowViewModel.PushAllBranchesAsync.
    /// </summary>
    public Func<Task>? OnPushRequested { get; set; }

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

    [RelayCommand]
    private async Task PushAsync()
    {
        if (OnPushRequested != null)
            await OnPushRequested();
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
