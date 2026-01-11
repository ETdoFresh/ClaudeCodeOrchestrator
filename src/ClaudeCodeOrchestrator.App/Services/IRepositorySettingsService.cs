using ClaudeCodeOrchestrator.App.Models;

namespace ClaudeCodeOrchestrator.App.Services;

/// <summary>
/// Service for managing per-repository settings.
/// </summary>
public interface IRepositorySettingsService
{
    /// <summary>
    /// Gets the current repository settings.
    /// </summary>
    RepositorySettings? Settings { get; }

    /// <summary>
    /// Gets whether an executable is configured.
    /// </summary>
    bool HasExecutable { get; }

    /// <summary>
    /// Gets the current repository path.
    /// </summary>
    string? RepositoryPath { get; }

    /// <summary>
    /// Event raised when settings change.
    /// </summary>
    event EventHandler? SettingsChanged;

    /// <summary>
    /// Loads settings for the specified repository.
    /// </summary>
    /// <param name="repositoryPath">The path to the repository.</param>
    void Load(string repositoryPath);

    /// <summary>
    /// Saves the current settings.
    /// </summary>
    void Save();

    /// <summary>
    /// Sets the executable and saves.
    /// </summary>
    /// <param name="executable">The executable path/command.</param>
    void SetExecutable(string? executable);

    /// <summary>
    /// Runs the configured executable in the specified working directory.
    /// </summary>
    /// <param name="workingDirectory">The directory to run the executable in.</param>
    /// <returns>True if the process was started successfully.</returns>
    Task<bool> RunExecutableAsync(string workingDirectory);

    /// <summary>
    /// Clears the current settings (called when closing a repository).
    /// </summary>
    void Clear();

    /// <summary>
    /// Gets whether VS Code is available on the system.
    /// </summary>
    bool IsVSCodeAvailable { get; }

    /// <summary>
    /// Opens VS Code in the specified working directory.
    /// </summary>
    /// <param name="workingDirectory">The directory to open in VS Code.</param>
    /// <returns>True if VS Code was launched successfully.</returns>
    Task<bool> OpenInVSCodeAsync(string workingDirectory);
}
