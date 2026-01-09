using ClaudeCodeOrchestrator.App.Models;
using ClaudeCodeOrchestrator.Core.Services;

namespace ClaudeCodeOrchestrator.App.Services;

/// <summary>
/// Service for showing dialogs and picking files/folders.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Sets the title generator service for use in dialogs.
    /// </summary>
    void SetTitleGeneratorService(ITitleGeneratorService titleGeneratorService);

    /// <summary>
    /// Shows a folder picker dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <returns>The selected folder path, or null if cancelled.</returns>
    Task<string?> ShowFolderPickerAsync(string title);

    /// <summary>
    /// Shows the new task dialog to get a task description with optional images.
    /// The user can generate and preview/edit the title and branch name before creating.
    /// </summary>
    /// <returns>The task input with text, images, and generated title/branch, or null if cancelled.</returns>
    Task<TaskInput?> ShowNewTaskDialogAsync();

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
}
