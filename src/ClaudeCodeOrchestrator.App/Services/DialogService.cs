using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using ClaudeCodeOrchestrator.App.Views.Dialogs;

namespace ClaudeCodeOrchestrator.App.Services;

/// <summary>
/// Implementation of dialog service using Avalonia dialogs.
/// </summary>
public sealed class DialogService : IDialogService
{
    private Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    /// <inheritdoc />
    public async Task<string?> ShowFolderPickerAsync(string title)
    {
        var window = GetMainWindow();
        if (window is null) return null;

        var storageProvider = window.StorageProvider;
        if (!storageProvider.CanPickFolder) return null;

        var result = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return result.Count > 0 ? result[0].Path.LocalPath : null;
    }

    /// <inheritdoc />
    public async Task ShowErrorAsync(string title, string message)
    {
        var window = GetMainWindow();
        if (window is null) return;

        var dialog = new MessageDialog(title, message, isError: true);
        await dialog.ShowDialog(window);
    }

    /// <inheritdoc />
    public async Task<bool> ShowConfirmAsync(string title, string message)
    {
        var window = GetMainWindow();
        if (window is null) return false;

        var dialog = new ConfirmDialog(title, message);
        return await dialog.ShowDialog<bool>(window);
    }

    /// <inheritdoc />
    public async Task ShowAboutAsync()
    {
        var window = GetMainWindow();
        if (window is null) return;

        var dialog = new AboutDialog();
        await dialog.ShowDialog(window);
    }

    /// <inheritdoc />
    public async Task<RepositorySettingsResult?> ShowRepositorySettingsAsync(
        string? currentExecutable,
        string? currentTaskPrefix,
        string? currentJobPrefix)
    {
        var window = GetMainWindow();
        if (window is null) return null;

        var dialog = new RepositorySettingsDialog();
        dialog.SetExecutable(currentExecutable);
        dialog.SetBranchPrefixes(currentTaskPrefix, currentJobPrefix);
        await dialog.ShowDialog(window);

        if (!dialog.WasSaved)
            return null;

        return new RepositorySettingsResult(
            dialog.Executable,
            dialog.TaskBranchPrefix,
            dialog.JobBranchPrefix);
    }
}
