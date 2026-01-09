using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
    /// Gets the total number of commits ahead across all worktrees (commits to push).
    /// </summary>
    public int TotalCommitsToPush => Worktrees.Sum(w => w.CommitsAhead);

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

        // Subscribe to collection changes to update TotalCommitsToPush
        Worktrees.CollectionChanged += OnWorktreesCollectionChanged;
    }

    private void OnWorktreesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Unsubscribe from old items
        if (e.OldItems != null)
        {
            foreach (WorktreeViewModel worktree in e.OldItems)
            {
                worktree.PropertyChanged -= OnWorktreePropertyChanged;
            }
        }

        // Subscribe to new items
        if (e.NewItems != null)
        {
            foreach (WorktreeViewModel worktree in e.NewItems)
            {
                worktree.PropertyChanged += OnWorktreePropertyChanged;
            }
        }

        OnPropertyChanged(nameof(TotalCommitsToPush));
    }

    private void OnWorktreePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorktreeViewModel.CommitsAhead))
        {
            OnPropertyChanged(nameof(TotalCommitsToPush));
        }
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
