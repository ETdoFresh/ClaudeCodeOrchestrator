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
    private TimeSpan _accumulatedDuration = TimeSpan.Zero;
    private DateTime? _currentTurnStartedAt;

    /// <summary>
    /// Gets or sets the accumulated active duration (for state preservation during worktree refresh).
    /// </summary>
    public TimeSpan AccumulatedDuration
    {
        get => _accumulatedDuration;
        set => _accumulatedDuration = value;
    }

    /// <summary>
    /// Gets or sets whether a turn is currently active (for state preservation during worktree refresh).
    /// </summary>
    public bool IsTurnActive => _currentTurnStartedAt != null;

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
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string? _title;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MergeCommand))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsReadyToMerge))]
    private WorktreeStatus _status = WorktreeStatus.Active;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MergeCommand))]
    private bool _hasUncommittedChanges;

    /// <summary>
    /// Indicates that a merge is in progress (e.g., Claude is resolving conflicts).
    /// When true, the merge button should be disabled.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MergeCommand))]
    private bool _isMergePending;

    [ObservableProperty]
    private int _commitsAhead;

    [ObservableProperty]
    private int _unpushedCommits;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionDurationText))]
    private bool _hasActiveSession;

    /// <summary>
    /// Indicates whether the session is currently processing (Claude is working).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    [NotifyPropertyChangedFor(nameof(IsReadyToMerge))]
    private bool _isProcessing;

    /// <summary>
    /// Indicates whether the session ended with an error.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    private bool _hasError;

    /// <summary>
    /// Indicates whether the session was interrupted/cancelled.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    private bool _wasInterrupted;

    /// <summary>
    /// Current iteration number for an active job (null if not part of a job).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IterationText))]
    private int? _currentIteration;

    /// <summary>
    /// Maximum iterations for an active job (null if not part of a job).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IterationText))]
    private int? _maxIterations;

    /// <summary>
    /// Gets the formatted iteration text (e.g., "Iteration 1/20").
    /// Shows current iteration for active jobs, or last iteration for historical jobs.
    /// Returns null if not part of a job.
    /// </summary>
    public string? IterationText
    {
        get
        {
            // Active job: show current iteration
            if (CurrentIteration.HasValue && MaxIterations.HasValue)
                return $"Iteration {CurrentIteration}/{MaxIterations}";

            // Historical job: show last iteration
            if (LastIteration.HasValue && JobMaxIterations.HasValue)
                return $"Ran {LastIteration}/{JobMaxIterations}";

            return null;
        }
    }

    [ObservableProperty]
    private string? _activeSessionId;

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
            var duration = SessionDuration;
            if (duration == TimeSpan.Zero && _accumulatedDuration == TimeSpan.Zero && _currentTurnStartedAt == null)
                return null;

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

    /// <summary>
    /// Callback for running the configured executable in this worktree.
    /// </summary>
    public Func<WorktreeViewModel, Task>? OnRunRequested { get; set; }

    /// <summary>
    /// Callback for opening VS Code in this worktree.
    /// </summary>
    public Func<WorktreeViewModel, Task>? OnOpenInVSCodeRequested { get; set; }

    /// <summary>
    /// Whether the run button should be visible (executable is configured).
    /// </summary>
    [ObservableProperty]
    private bool _canRun;

    /// <summary>
    /// Whether the VS Code button should be visible (VS Code is available).
    /// </summary>
    [ObservableProperty]
    private bool _canOpenInVSCode;

    public string StatusText => DisplayStatus.Text;

    /// <summary>
    /// Gets the display status based on processing state and git status.
    /// States: Processing > Mergable (has commits) > Completed > Failed > Interrupted
    /// </summary>
    public (string Text, string Color) DisplayStatus
    {
        get
        {
            // If processing, always show Processing
            if (IsProcessing)
                return ("Processing", "#007ACC"); // Blue

            // If has commits ahead, show Mergable (ready to merge)
            if (Status == WorktreeStatus.ReadyToMerge)
                return ("Mergable", "#4CAF50"); // Green

            // If session had an error, show Failed
            if (HasError)
                return ("Failed", "#F44336"); // Red

            // If session was interrupted/cancelled, show Interrupted
            if (WasInterrupted)
                return ("Interrupted", "#FF9800"); // Orange

            // Default to Completed (session done or waiting)
            return ("Completed", "#9E9E9E"); // Gray
        }
    }

    /// <summary>
    /// Indicates if the worktree is ready to merge (not currently processing and status is ReadyToMerge).
    /// Used to show the prominent Merge button in the top-right area.
    /// Note: We check IsProcessing rather than HasActiveSession because a completed session
    /// (with HasActiveSession=true) should still show the merge button if it has commits to merge.
    /// </summary>
    public bool IsReadyToMerge => !IsProcessing && Status == WorktreeStatus.ReadyToMerge;

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

    private bool CanMerge() => Status == WorktreeStatus.ReadyToMerge && !HasUncommittedChanges && !IsMergePending;

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (OnDeleteRequested != null)
            await OnDeleteRequested(this);
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (OnRunRequested != null)
            await OnRunRequested(this);
    }

    [RelayCommand]
    private async Task OpenInVSCodeAsync()
    {
        if (OnOpenInVSCodeRequested != null)
            await OnOpenInVSCodeRequested(this);
    }

    /// <summary>
    /// Display title - uses generated title if available, otherwise task description.
    /// </summary>
    public string DisplayTitle => !string.IsNullOrEmpty(Title) ? Title
        : (TaskDescription.Length > 50 ? TaskDescription[..47] + "..." : TaskDescription);

    /// <summary>
    /// Starts tracking a new turn (when Claude begins processing).
    /// </summary>
    public void StartTurn()
    {
        if (_currentTurnStartedAt != null) return; // Already in a turn

        _currentTurnStartedAt = DateTime.UtcNow;
        UpdateDuration();

        // Start/restart the display timer if not running
        if (_durationTimer == null)
        {
            _durationTimer = new System.Timers.Timer(1000);
            _durationTimer.Elapsed += OnDurationTimerElapsed;
            _durationTimer.AutoReset = true;
            _durationTimer.Start();
        }
    }

    /// <summary>
    /// Ends the current turn and accumulates the elapsed time.
    /// </summary>
    public void EndTurn()
    {
        if (_currentTurnStartedAt == null) return;

        // Accumulate time from this turn
        _accumulatedDuration += DateTime.UtcNow - _currentTurnStartedAt.Value;
        _currentTurnStartedAt = null;

        UpdateDuration(); // Update display with final accumulated value
        // Note: Timer keeps running to show stable duration
    }

    /// <summary>
    /// Resets the timer completely (for session end or new session).
    /// </summary>
    public void ResetTimer()
    {
        _durationTimer?.Stop();
        _durationTimer?.Dispose();
        _durationTimer = null;

        _accumulatedDuration = TimeSpan.Zero;
        _currentTurnStartedAt = null;
        SessionDuration = TimeSpan.Zero;
    }

    private void OnDurationTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        UpdateDuration();
    }

    private void UpdateDuration()
    {
        var duration = _accumulatedDuration;
        if (_currentTurnStartedAt != null)
        {
            duration += DateTime.UtcNow - _currentTurnStartedAt.Value;
        }
        SessionDuration = duration;
    }

    /// <summary>
    /// Whether this worktree was used as a job (for persistence across app restarts).
    /// </summary>
    [ObservableProperty]
    private bool _wasJob;

    /// <summary>
    /// The last iteration number when the job was running (for display in history).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IterationText))]
    private int? _lastIteration;

    /// <summary>
    /// The max iterations configured when the job was running (for display in history).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IterationText))]
    private int? _jobMaxIterations;

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
            CommitsAhead = info.CommitsAhead,
            UnpushedCommits = info.UnpushedCommits,
            // Job metadata for persistence
            WasJob = info.WasJob,
            LastIteration = info.LastIteration,
            JobMaxIterations = info.JobMaxIterations
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

        ResetTimer();
    }
}
