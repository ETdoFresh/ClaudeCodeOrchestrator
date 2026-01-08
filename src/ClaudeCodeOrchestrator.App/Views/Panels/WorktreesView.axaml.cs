using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ClaudeCodeOrchestrator.App.ViewModels;
using ClaudeCodeOrchestrator.App.ViewModels.Docking;
using ClaudeCodeOrchestrator.Git.Models;

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

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Subscribe to double-click to open session
        WorktreesList.DoubleTapped += OnDoubleTapped;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        WorktreesList.DoubleTapped -= OnDoubleTapped;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not WorktreesViewModel vm) return;

        if (vm.SelectedWorktree is WorktreeViewModel worktree)
        {
            vm.SelectWorktreeCommand.Execute(worktree);
        }
    }
}
