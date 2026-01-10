namespace ClaudeCodeOrchestrator.App.Services;

/// <summary>
/// Service for showing dialogs and picking files/folders.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows a folder picker dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <returns>The selected folder path, or null if cancelled.</returns>
    Task<string?> ShowFolderPickerAsync(string title);

    /// <summary>
    /// Shows an error dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The error message.</param>
    Task ShowErrorAsync(string title, string message);

    /// <summary>
    /// Shows a confirmation dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The confirmation message.</param>
    /// <returns>True if confirmed, false otherwise.</returns>
    Task<bool> ShowConfirmAsync(string title, string message);

    /// <summary>
    /// Shows the About dialog with application information.
    /// </summary>
    Task ShowAboutAsync();

    /// <summary>
    /// Shows the repository settings dialog.
    /// </summary>
    /// <param name="currentExecutable">The current executable setting.</param>
    /// <returns>The new executable setting if saved, or null if cancelled.</returns>
    Task<string?> ShowRepositorySettingsAsync(string? currentExecutable);
}
