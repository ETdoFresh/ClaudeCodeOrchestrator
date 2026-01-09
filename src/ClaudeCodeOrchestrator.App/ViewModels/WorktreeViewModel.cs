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
    private string? _title;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MergeCommand))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private WorktreeStatus _status = WorktreeStatus.Active;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MergeCommand))]
    private bool _hasUncommittedChanges;

    [ObservableProperty]
    private int _commitsAhead;

    [ObservableProperty]
    private bool _hasActiveSession;

    [ObservableProperty]
    private string? _activeSessionId;

    /// <summary>
    /// Callback for opening a session for this worktree.
    /// </summary>
    public Func<WorktreeViewModel, Task>? OnOpenSessionRequested { get; set; }

    /// <summary>
    /// Callback for merging this worktree.
    /// </summary>
    public Func<WorktreeViewModel, Task>? OnMergeRequested { get; set; }

    /// <summary>
    /// Callback for deleting this worktree.
    /// </summary>
    public Func<WorktreeViewModel, Task>? OnDeleteRequested { get; set; }

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
        if (OnOpenSessionRequested != null)
            await OnOpenSessionRequested(this);
    }

    [RelayCommand(CanExecute = nameof(CanMerge))]
    private async Task MergeAsync()
    {
        if (OnMergeRequested != null)
            await OnMergeRequested(this);
    }

    private bool CanMerge() => Status == WorktreeStatus.ReadyToMerge && !HasUncommittedChanges;

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (OnDeleteRequested != null)
            await OnDeleteRequested(this);
    }

    /// <summary>
    /// Display title - uses generated title if available, otherwise task description.
    /// </summary>
    public string DisplayTitle => !string.IsNullOrEmpty(Title) ? Title
        : (TaskDescription.Length > 50 ? TaskDescription[..47] + "..." : TaskDescription);

    public static WorktreeViewModel FromModel(WorktreeInfo info)
    {
        var vm = new WorktreeViewModel
        {
            Id = info.Id,
            Path = info.Path,
            BranchName = info.BranchName,
            BaseBranch = info.BaseBranch,
            TaskDescription = info.TaskDescription,
            Title = info.Title,
            CommitsAhead = info.CommitsAhead
        };
        // Set these last to trigger NotifyCanExecuteChangedFor
        vm.Status = info.Status;
        vm.HasUncommittedChanges = info.HasUncommittedChanges;
        return vm;
    }
}
