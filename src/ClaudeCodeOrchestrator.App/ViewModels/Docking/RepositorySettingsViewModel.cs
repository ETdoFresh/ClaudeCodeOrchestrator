using CommunityToolkit.Mvvm.ComponentModel;
using ClaudeCodeOrchestrator.App.Services;

namespace ClaudeCodeOrchestrator.App.ViewModels.Docking;

/// <summary>
/// Repository settings panel view model for configuring per-repository preferences.
/// </summary>
public partial class RepositorySettingsViewModel : ToolViewModelBase
{
    private readonly IRepositorySettingsService _repositorySettingsService;
    private bool _isLoading;

    [ObservableProperty]
    private string? _executable;

    [ObservableProperty]
    private bool _hasRepository;

    public RepositorySettingsViewModel(IRepositorySettingsService repositorySettingsService)
    {
        _repositorySettingsService = repositorySettingsService;
        Id = "RepositorySettings";
        Title = "Repo";

        // Subscribe to settings changes from the service
        _repositorySettingsService.SettingsChanged += OnSettingsChanged;

        LoadSettings();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        LoadSettings();
    }

    private void LoadSettings()
    {
        _isLoading = true;
        try
        {
            HasRepository = _repositorySettingsService.RepositoryPath != null;
            Executable = _repositorySettingsService.Settings?.Executable;
        }
        finally
        {
            _isLoading = false;
        }
    }

    partial void OnExecutableChanged(string? value)
    {
        if (_isLoading) return;
        _repositorySettingsService.SetExecutable(value);
    }
}
