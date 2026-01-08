using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeCodeOrchestrator.App.ViewModels.Docking;

/// <summary>
/// Output panel view model.
/// </summary>
public partial class OutputViewModel : ToolViewModelBase
{
    [ObservableProperty]
    private string _outputText = string.Empty;

    public OutputViewModel()
    {
        Id = "Output";
        Title = "Output";
    }

    public void AppendLine(string text)
    {
        OutputText += text + Environment.NewLine;
    }

    [RelayCommand]
    private void Clear()
    {
        OutputText = string.Empty;
    }
}
