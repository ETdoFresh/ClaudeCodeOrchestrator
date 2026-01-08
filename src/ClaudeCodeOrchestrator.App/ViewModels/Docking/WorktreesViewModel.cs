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

    public WorktreesViewModel()
    {
        Id = "Worktrees";
        Title = "Worktrees";
    }

    [RelayCommand]
    private async Task CreateTaskAsync()
    {
        // TODO: Show new task dialog
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        // TODO: Refresh worktrees from git
    }
}
