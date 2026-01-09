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

    private DateTime _lastSelectionTime;
    private const int DoubleClickThresholdMs = 300;

    public WorktreesView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Subscribe to selection changed for single-click (preview)
        WorktreesList.SelectionChanged += OnSelectionChanged;

        // Subscribe to double-click to open session
        WorktreesList.DoubleTapped += OnDoubleTapped;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        WorktreesList.SelectionChanged -= OnSelectionChanged;
        WorktreesList.DoubleTapped -= OnDoubleTapped;
    }

    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not WorktreesViewModel vm) return;
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is not WorktreeViewModel worktree) return;

        // Record selection time to detect double-clicks
        _lastSelectionTime = DateTime.UtcNow;

        // Delay briefly to see if this is part of a double-click
        await Task.Delay(DoubleClickThresholdMs);

        // Check if a double-click happened in the meantime (time would be updated)
        var elapsed = (DateTime.UtcNow - _lastSelectionTime).TotalMilliseconds;
        if (elapsed < DoubleClickThresholdMs)
        {
            // A double-click is occurring, skip the preview action
            return;
        }

        vm.SelectWorktreeCommand.Execute(worktree);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not WorktreesViewModel vm) return;

        // Update time to prevent the delayed selection handler from running
        _lastSelectionTime = DateTime.UtcNow;

        if (vm.SelectedWorktree is WorktreeViewModel worktree)
        {
            // Double-click opens as persistent (non-preview)
            vm.OpenWorktreeCommand.Execute(worktree);
        }
    }
}
