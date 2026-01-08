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
    /// Loads settings from storage.
    /// </summary>
    void Load();

    /// <summary>
    /// Saves settings to storage.
    /// </summary>
    void Save();
}
