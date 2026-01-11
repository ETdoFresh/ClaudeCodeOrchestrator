using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeCodeOrchestrator.App.ViewModels.Docking;

/// <summary>
/// Session continuation options for jobs.
/// </summary>
public enum SessionOption
{
    ResumeSession,
    NewSession
}

/// <summary>
/// Prompt template options for jobs.
/// </summary>
public enum PromptOption
{
    ContinueUntilComplete,
    ContinueWithSummary,
    ReviewAndDecide
}

/// <summary>
/// Represents a prompt option in the dropdown.
/// </summary>
public class PromptOptionItem
{
    public required PromptOption Value { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public bool RequiresResumeSession { get; init; }
}

/// <summary>
/// Jobs panel view model for long-running worktree jobs with extra configuration.
/// </summary>
public partial class JobsViewModel : ToolViewModelBase
{
    [ObservableProperty]
    private WorktreeViewModel? _selectedWorktree;

    [ObservableProperty]
    private int _maxIterations = 20;

    [ObservableProperty]
    private int _sessionOptionIndex = 0;

    /// <summary>
    /// Gets the SessionOption enum value from the selected index.
    /// </summary>
    public SessionOption SessionOption => (SessionOption)SessionOptionIndex;

    [ObservableProperty]
    private PromptOptionItem? _selectedPromptOption;

    [ObservableProperty]
    private bool _askToCommitWhenDone = true;

    public ObservableCollection<WorktreeViewModel> Worktrees { get; } = new();

    public ObservableCollection<PromptOptionItem> PromptOptions { get; } = new();

    /// <summary>
    /// Callback to invoke when the user requests to start a job.
    /// </summary>
    public Func<WorktreeViewModel, JobConfiguration, Task>? OnStartJobRequested { get; set; }

    /// <summary>
    /// Callback to invoke when a worktree is selected.
    /// </summary>
    public Func<WorktreeViewModel, bool, Task>? OnWorktreeSelected { get; set; }

    public JobsViewModel()
    {
        Id = "Jobs";
        Title = "Jobs";

        // Initialize prompt options
        PromptOptions.Add(new PromptOptionItem
        {
            Value = PromptOption.ContinueUntilComplete,
            DisplayName = "Continue until complete",
            Description = "Please continue until complete",
            RequiresResumeSession = true
        });
        PromptOptions.Add(new PromptOptionItem
        {
            Value = PromptOption.ContinueWithSummary,
            DisplayName = "Continue with summary",
            Description = "Please continue working on this task: {PreviousConversationSummary}",
            RequiresResumeSession = false
        });
        PromptOptions.Add(new PromptOptionItem
        {
            Value = PromptOption.ReviewAndDecide,
            DisplayName = "Review and decide (Default)",
            Description = "Please review the initial task and decide what to work on next, then implement until complete",
            RequiresResumeSession = false
        });

        SelectedPromptOption = PromptOptions[2]; // Default to ReviewAndDecide
    }

    partial void OnSessionOptionIndexChanged(int value)
    {
        // If switching to NewSession and current prompt requires resume, switch to default
        if ((SessionOption)value == SessionOption.NewSession && SelectedPromptOption?.RequiresResumeSession == true)
        {
            SelectedPromptOption = PromptOptions[2]; // ReviewAndDecide
        }
    }

    /// <summary>
    /// Gets whether the selected prompt option is available based on session option.
    /// </summary>
    public bool IsPromptOptionAvailable(PromptOptionItem option)
    {
        if (option.RequiresResumeSession && SessionOption == SessionOption.NewSession)
            return false;
        return true;
    }

    [RelayCommand]
    private async Task SelectWorktreeAsync(WorktreeViewModel worktree)
    {
        SelectedWorktree = worktree;
        if (OnWorktreeSelected != null)
            await OnWorktreeSelected(worktree, true);
    }

    [RelayCommand]
    private async Task StartJobAsync(WorktreeViewModel worktree)
    {
        if (OnStartJobRequested == null || SelectedPromptOption == null) return;

        var config = new JobConfiguration
        {
            MaxIterations = MaxIterations,
            SessionOption = SessionOption,
            PromptOption = SelectedPromptOption.Value,
            AskToCommitWhenDone = AskToCommitWhenDone
        };

        await OnStartJobRequested(worktree, config);
    }
}

/// <summary>
/// Configuration for a long-running job.
/// </summary>
public class JobConfiguration
{
    public int MaxIterations { get; init; } = 20;
    public SessionOption SessionOption { get; init; } = SessionOption.ResumeSession;
    public PromptOption PromptOption { get; init; } = PromptOption.ReviewAndDecide;
    public bool AskToCommitWhenDone { get; init; } = true;

    /// <summary>
    /// Generates the prompt text based on configuration and worktree context.
    /// </summary>
    public string GeneratePrompt(string? initialPrompt = null, string? previousSummary = null)
    {
        return PromptOption switch
        {
            PromptOption.ContinueUntilComplete => "Please continue until complete.",
            PromptOption.ContinueWithSummary => string.IsNullOrEmpty(previousSummary)
                ? "Please continue working on this task."
                : $"Please continue working on this task: {previousSummary}",
            PromptOption.ReviewAndDecide => string.IsNullOrEmpty(initialPrompt)
                ? "Please review the current state and decide what task to work on next, then implement until complete."
                : $"Please review the following task and decide what to work on next, then implement until complete:\n\n{initialPrompt}",
            _ => "Please continue."
        };
    }
}
