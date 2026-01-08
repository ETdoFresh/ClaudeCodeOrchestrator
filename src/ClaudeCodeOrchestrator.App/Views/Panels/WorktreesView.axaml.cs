using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ClaudeCodeOrchestrator.Git.Models;
using System.Globalization;

namespace ClaudeCodeOrchestrator.App.Views.Panels;

public partial class WorktreesView : UserControl
{
    public static FuncValueConverter<WorktreeStatus, IBrush> StatusToBrushConverter { get; } =
        new(status => status switch
        {
            WorktreeStatus.Active => new SolidColorBrush(Color.Parse("#0E639C")),
            WorktreeStatus.HasChanges => new SolidColorBrush(Color.Parse("#CCA700")),
            WorktreeStatus.ReadyToMerge => new SolidColorBrush(Color.Parse("#388A34")),
            WorktreeStatus.Merged => new SolidColorBrush(Color.Parse("#4EC9B0")),
            WorktreeStatus.Locked => new SolidColorBrush(Color.Parse("#6C2022")),
            _ => new SolidColorBrush(Color.Parse("#666666"))
        });

    public WorktreesView()
    {
        InitializeComponent();
    }
}
