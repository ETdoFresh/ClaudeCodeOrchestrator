using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeCodeOrchestrator.App.ViewModels.Docking;

/// <summary>
/// Represents a panel option in the left panel dropdown.
/// </summary>
public class PanelOption
{
    public required string Name { get; init; }
    public required object ViewModel { get; init; }
}

/// <summary>
/// View model for the left panel with dropdown navigation.
/// </summary>
public partial class LeftPanelViewModel : ToolViewModelBase
{
    [ObservableProperty]
    private PanelOption? _selectedPanel;

    public ObservableCollection<PanelOption> PanelOptions { get; } = new();

    public WorktreesViewModel? WorktreesViewModel { get; private set; }
    public JobsViewModel? JobsViewModel { get; private set; }
    public FileBrowserViewModel? FileBrowserViewModel { get; private set; }
    public DiffBrowserViewModel? DiffBrowserViewModel { get; private set; }
    public SettingsViewModel? SettingsViewModel { get; private set; }

    public LeftPanelViewModel()
    {
        Id = "LeftPanel";
        Title = "Left Panel";
    }

    /// <summary>
    /// Initializes the panel with the child view models.
    /// </summary>
    public void Initialize(
        WorktreesViewModel worktreesViewModel,
        JobsViewModel jobsViewModel,
        FileBrowserViewModel fileBrowserViewModel,
        DiffBrowserViewModel diffBrowserViewModel,
        SettingsViewModel settingsViewModel)
    {
        WorktreesViewModel = worktreesViewModel;
        JobsViewModel = jobsViewModel;
        FileBrowserViewModel = fileBrowserViewModel;
        DiffBrowserViewModel = diffBrowserViewModel;
        SettingsViewModel = settingsViewModel;

        PanelOptions.Clear();
        PanelOptions.Add(new PanelOption { Name = "Worktrees", ViewModel = worktreesViewModel });
        PanelOptions.Add(new PanelOption { Name = "Jobs", ViewModel = jobsViewModel });
        PanelOptions.Add(new PanelOption { Name = "Explorer", ViewModel = fileBrowserViewModel });
        PanelOptions.Add(new PanelOption { Name = "Diff", ViewModel = diffBrowserViewModel });
        PanelOptions.Add(new PanelOption { Name = "Settings", ViewModel = settingsViewModel });

        // Select first panel by default
        SelectedPanel = PanelOptions[0];
    }

    /// <summary>
    /// Selects a panel by its view model.
    /// </summary>
    public void SelectPanel(object viewModel)
    {
        var panel = PanelOptions.FirstOrDefault(p => p.ViewModel == viewModel);
        if (panel != null)
        {
            SelectedPanel = panel;
        }
    }

    /// <summary>
    /// Selects a panel by name.
    /// </summary>
    public void SelectPanelByName(string name)
    {
        var panel = PanelOptions.FirstOrDefault(p => p.Name == name);
        if (panel != null)
        {
            SelectedPanel = panel;
        }
    }
}
