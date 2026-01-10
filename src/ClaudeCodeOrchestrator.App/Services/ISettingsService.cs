namespace ClaudeCodeOrchestrator.App.Services;

/// <summary>
/// Service for persisting application settings.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Gets the last opened repository path.
    /// </summary>
    string? LastRepositoryPath { get; }

    /// <summary>
    /// Sets and persists the last opened repository path.
    /// </summary>
    void SetLastRepositoryPath(string? path);

    /// <summary>
    /// Gets the list of recently opened repository paths (up to 4).
    /// </summary>
    IReadOnlyList<string> RecentRepositories { get; }

    /// <summary>
    /// Adds a repository path to the recent repositories list.
    /// </summary>
    void AddRecentRepository(string path);

    /// <summary>
    /// Gets or sets whether to show confirmation dialog before merging worktrees.
    /// </summary>
    bool ShowMergeConfirmation { get; set; }

    /// <summary>
    /// Gets or sets whether to show confirmation dialog before deleting worktrees.
    /// </summary>
    bool ShowDeleteConfirmation { get; set; }

    /// <summary>
    /// Gets or sets whether to show confirmation dialog when closing sessions with unsaved changes.
    /// </summary>
    bool ShowCloseSessionConfirmation { get; set; }

    /// <summary>
    /// Gets or sets whether to automatically open sessions when creating new worktrees.
    /// </summary>
    bool AutoOpenSessionOnWorktreeCreate { get; set; }

    /// <summary>
    /// Gets or sets whether to show the output panel by default.
    /// </summary>
    bool ShowOutputPanelByDefault { get; set; }

    /// <summary>
    /// Gets or sets whether to enable compact mode in the worktree list.
    /// </summary>
    bool CompactWorktreeList { get; set; }

    /// <summary>
    /// Gets or sets whether to show worktree status badges.
    /// </summary>
    bool ShowWorktreeStatusBadges { get; set; }

    /// <summary>
    /// Gets or sets whether to auto-save session history.
    /// </summary>
    bool AutoSaveSessionHistory { get; set; }

    /// <summary>
    /// Gets or sets the cached push badge count for immediate display on startup.
    /// </summary>
    int CachedPushBadgeCount { get; set; }

    /// <summary>
    /// Gets or sets the OpenRouter API key for title generation.
    /// When set, this takes priority over .env file and environment variables.
    /// </summary>
    string? OpenRouterApiKey { get; set; }

    /// <summary>
    /// Loads settings from storage.
    /// </summary>
    void Load();

    /// <summary>
    /// Saves settings to storage.
    /// </summary>
    void Save();

    /// <summary>
    /// Event raised when a setting changes.
    /// </summary>
    event EventHandler<SettingChangedEventArgs>? SettingChanged;
}

/// <summary>
/// Event arguments for setting change notifications.
/// </summary>
public class SettingChangedEventArgs : EventArgs
{
    public string SettingName { get; }
    public object? OldValue { get; }
    public object? NewValue { get; }

    public SettingChangedEventArgs(string settingName, object? oldValue, object? newValue)
    {
        SettingName = settingName;
        OldValue = oldValue;
        NewValue = newValue;
    }
}
