using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeCodeOrchestrator.Git.Models;

namespace ClaudeCodeOrchestrator.App.ViewModels;

/// <summary>
/// View model for a worktree.
/// </summary>
public partial class WorktreeViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private string _branchName = string.Empty;

    [ObservableProperty]
    private string _baseBranch = string.Empty;

    [ObservableProperty]
    private string _taskDescription = string.Empty;

    [ObservableProperty]
    private WorktreeStatus _status = WorktreeStatus.Active;

    [ObservableProperty]
    private bool _hasUncommittedChanges;

    [ObservableProperty]
    private int _commitsAhead;

    [ObservableProperty]
    private bool _hasActiveSession;

    [ObservableProperty]
    private string? _activeSessionId;

    public string StatusText => Status switch
    {
        WorktreeStatus.Active => "Active",
        WorktreeStatus.HasChanges => $"Changes ({CommitsAhead} ahead)",
        WorktreeStatus.ReadyToMerge => $"Ready to merge ({CommitsAhead} commits)",
        WorktreeStatus.Merged => "Merged",
        WorktreeStatus.Locked => "Locked",
        _ => "Unknown"
    };

    [RelayCommand]
    private async Task OpenSessionAsync()
    {
        // TODO: Open or create session for this worktree
    }

    [RelayCommand(CanExecute = nameof(CanMerge))]
    private async Task MergeAsync()
    {
        // TODO: Merge worktree to base branch
    }

    private bool CanMerge() => Status == WorktreeStatus.ReadyToMerge && !HasUncommittedChanges;

    [RelayCommand]
    private async Task DeleteAsync()
    {
        // TODO: Delete worktree
    }

    public static WorktreeViewModel FromModel(WorktreeInfo info) => new()
    {
        Id = info.Id,
        Path = info.Path,
        BranchName = info.BranchName,
        BaseBranch = info.BaseBranch,
        TaskDescription = info.TaskDescription,
        Status = info.Status,
        HasUncommittedChanges = info.HasUncommittedChanges,
        CommitsAhead = info.CommitsAhead
    };
}
