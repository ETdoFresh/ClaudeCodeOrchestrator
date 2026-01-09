using CommunityToolkit.Mvvm.ComponentModel;
using ClaudeCodeOrchestrator.App.Services;

namespace ClaudeCodeOrchestrator.App.ViewModels.Docking;

/// <summary>
/// Settings panel view model for configuring application preferences.
/// </summary>
public partial class SettingsViewModel : ToolViewModelBase
{
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private bool _showMergeConfirmation;

    [ObservableProperty]
    private bool _showDeleteConfirmation;

    [ObservableProperty]
    private bool _showCloseSessionConfirmation;

    [ObservableProperty]
    private bool _autoOpenSessionOnWorktreeCreate;

    [ObservableProperty]
    private bool _showOutputPanelByDefault;

    [ObservableProperty]
    private bool _compactWorktreeList;

    [ObservableProperty]
    private bool _showWorktreeStatusBadges;

    [ObservableProperty]
    private bool _autoSaveSessionHistory;

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        Id = "Settings";
        Title = "Settings";

        LoadSettings();
    }

    private void LoadSettings()
    {
        ShowMergeConfirmation = _settingsService.ShowMergeConfirmation;
        ShowDeleteConfirmation = _settingsService.ShowDeleteConfirmation;
        ShowCloseSessionConfirmation = _settingsService.ShowCloseSessionConfirmation;
        AutoOpenSessionOnWorktreeCreate = _settingsService.AutoOpenSessionOnWorktreeCreate;
        ShowOutputPanelByDefault = _settingsService.ShowOutputPanelByDefault;
        CompactWorktreeList = _settingsService.CompactWorktreeList;
        ShowWorktreeStatusBadges = _settingsService.ShowWorktreeStatusBadges;
        AutoSaveSessionHistory = _settingsService.AutoSaveSessionHistory;
    }

    partial void OnShowMergeConfirmationChanged(bool value)
    {
        _settingsService.ShowMergeConfirmation = value;
    }

    partial void OnShowDeleteConfirmationChanged(bool value)
    {
        _settingsService.ShowDeleteConfirmation = value;
    }

    partial void OnShowCloseSessionConfirmationChanged(bool value)
    {
        _settingsService.ShowCloseSessionConfirmation = value;
    }

    partial void OnAutoOpenSessionOnWorktreeCreateChanged(bool value)
    {
        _settingsService.AutoOpenSessionOnWorktreeCreate = value;
    }

    partial void OnShowOutputPanelByDefaultChanged(bool value)
    {
        _settingsService.ShowOutputPanelByDefault = value;
    }

    partial void OnCompactWorktreeListChanged(bool value)
    {
        _settingsService.CompactWorktreeList = value;
    }

    partial void OnShowWorktreeStatusBadgesChanged(bool value)
    {
        _settingsService.ShowWorktreeStatusBadges = value;
    }

    partial void OnAutoSaveSessionHistoryChanged(bool value)
    {
        _settingsService.AutoSaveSessionHistory = value;
    }
}
