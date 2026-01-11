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
    ReviewAndDecide,
    Other
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
    private bool _commitOnEndOfTurn = true;

    [ObservableProperty]
    private string _customPromptTitle = string.Empty;

    [ObservableProperty]
    private string _customPromptText = string.Empty;

    [ObservableProperty]
    private bool _isCustomPromptSelected;

    public ObservableCollection<WorktreeViewModel> Worktrees { get; } = new();

    public ObservableCollection<PromptOptionItem> PromptOptions { get; } = new();

    public ObservableCollection<SavedPrompt> SavedPrompts { get; } = new();

    [ObservableProperty]
    private SavedPrompt? _selectedSavedPrompt;

    /// <summary>
    /// Callback to invoke when the user requests to start a job on an existing worktree.
    /// </summary>
    public Func<WorktreeViewModel, JobConfiguration, Task>? OnStartJobRequested { get; set; }

    /// <summary>
    /// Callback to invoke when the user requests to create a new job (creates worktree and starts).
    /// </summary>
    public Func<JobConfiguration, Task>? OnCreateNewJobRequested { get; set; }

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
        PromptOptions.Add(new PromptOptionItem
        {
            Value = PromptOption.Other,
            DisplayName = "Other...",
            Description = "Specify a custom title and prompt",
            RequiresResumeSession = false
        });

        SelectedPromptOption = PromptOptions[2]; // Default to ReviewAndDecide
    }

    partial void OnSelectedPromptOptionChanged(PromptOptionItem? value)
    {
        IsCustomPromptSelected = value?.Value == PromptOption.Other;
    }

    partial void OnSelectedSavedPromptChanged(SavedPrompt? value)
    {
        if (value != null)
        {
            CustomPromptTitle = value.Title;
            CustomPromptText = value.PromptText;
        }
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
    private async Task CreateNewJobAsync()
    {
        if (OnCreateNewJobRequested == null || SelectedPromptOption == null) return;

        var config = new JobConfiguration
        {
            MaxIterations = MaxIterations,
            SessionOption = SessionOption,
            PromptOption = SelectedPromptOption.Value,
            CommitOnEndOfTurn = CommitOnEndOfTurn,
            CustomPromptTitle = SelectedPromptOption.Value == PromptOption.Other ? CustomPromptTitle : null,
            CustomPromptText = SelectedPromptOption.Value == PromptOption.Other ? CustomPromptText : null
        };

        await OnCreateNewJobRequested(config);
    }

    [RelayCommand]
    private void SaveCustomPrompt()
    {
        if (string.IsNullOrWhiteSpace(CustomPromptTitle) || string.IsNullOrWhiteSpace(CustomPromptText))
            return;

        // Check if a prompt with this title already exists
        var existing = SavedPrompts.FirstOrDefault(p => p.Title == CustomPromptTitle);
        if (existing != null)
        {
            existing.PromptText = CustomPromptText;
        }
        else
        {
            var newPrompt = new SavedPrompt { Title = CustomPromptTitle, PromptText = CustomPromptText };
            SavedPrompts.Add(newPrompt);
            SelectedSavedPrompt = newPrompt;
        }

        OnSavePromptsRequested?.Invoke();
    }

    [RelayCommand]
    private void DeleteSavedPrompt()
    {
        if (SelectedSavedPrompt == null) return;

        SavedPrompts.Remove(SelectedSavedPrompt);
        SelectedSavedPrompt = null;
        CustomPromptTitle = string.Empty;
        CustomPromptText = string.Empty;

        OnSavePromptsRequested?.Invoke();
    }

    /// <summary>
    /// Callback to save prompts to persistent storage.
    /// </summary>
    public Action? OnSavePromptsRequested { get; set; }

    [RelayCommand]
    private async Task StartJobAsync(WorktreeViewModel worktree)
    {
        if (OnStartJobRequested == null || SelectedPromptOption == null) return;

        var config = new JobConfiguration
        {
            MaxIterations = MaxIterations,
            SessionOption = SessionOption,
            PromptOption = SelectedPromptOption.Value,
            CommitOnEndOfTurn = CommitOnEndOfTurn,
            CustomPromptTitle = SelectedPromptOption.Value == PromptOption.Other ? CustomPromptTitle : null,
            CustomPromptText = SelectedPromptOption.Value == PromptOption.Other ? CustomPromptText : null
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
    public bool CommitOnEndOfTurn { get; init; } = true;
    public string? CustomPromptTitle { get; init; }
    public string? CustomPromptText { get; init; }

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
            PromptOption.Other => CustomPromptText ?? "Please continue.",
            _ => "Please continue."
        };
    }
}

/// <summary>
/// Represents a saved custom prompt.
/// </summary>
public class SavedPrompt
{
    public required string Title { get; set; }
    public required string PromptText { get; set; }
}
