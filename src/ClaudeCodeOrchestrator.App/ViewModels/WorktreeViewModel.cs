using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeCodeOrchestrator.Git.Models;

namespace ClaudeCodeOrchestrator.App.ViewModels;

/// <summary>
/// View model for a worktree.
/// </summary>
public partial class WorktreeViewModel : ViewModelBase, IDisposable
{
    private System.Timers.Timer? _durationTimer;
    private bool _disposed;

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
    [NotifyPropertyChangedFor(nameof(SessionDurationText))]
    private bool _hasActiveSession;

    [ObservableProperty]
    private string? _activeSessionId;

    [ObservableProperty]
    private DateTime? _sessionStartedAt;

    [ObservableProperty]
    private DateTime? _sessionEndedAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionDurationText))]
    private TimeSpan _sessionDuration;

    /// <summary>
    /// Gets the formatted session duration text.
    /// </summary>
    public string? SessionDurationText
    {
        get
        {
            if (SessionStartedAt == null)
                return null;

            var duration = SessionDuration;
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h {duration.Minutes}m";
            if (duration.TotalMinutes >= 1)
                return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
            return $"{(int)duration.TotalSeconds}s";
        }
    }

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

    /// <summary>
    /// Starts tracking session duration with a timer.
    /// </summary>
    public void StartSessionTimer(DateTime startedAt)
    {
        SessionStartedAt = startedAt;
        SessionEndedAt = null;
        UpdateDuration();

        // Start timer to update duration every second
        _durationTimer?.Dispose();
        _durationTimer = new System.Timers.Timer(1000);
        _durationTimer.Elapsed += OnDurationTimerElapsed;
        _durationTimer.AutoReset = true;
        _durationTimer.Start();
    }

    /// <summary>
    /// Stops the session timer and records the end time.
    /// </summary>
    public void StopSessionTimer(DateTime? endedAt = null)
    {
        _durationTimer?.Stop();
        _durationTimer?.Dispose();
        _durationTimer = null;

        SessionEndedAt = endedAt ?? DateTime.Now;
        UpdateDuration();
    }

    private void OnDurationTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        UpdateDuration();
    }

    private void UpdateDuration()
    {
        if (SessionStartedAt == null)
        {
            SessionDuration = TimeSpan.Zero;
            return;
        }

        var endTime = SessionEndedAt ?? DateTime.Now;
        SessionDuration = endTime - SessionStartedAt.Value;
    }

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _durationTimer?.Stop();
        _durationTimer?.Dispose();
        _durationTimer = null;
    }
}
