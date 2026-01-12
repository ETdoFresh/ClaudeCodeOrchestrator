using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ClaudeCodeOrchestrator.App.ViewModels;
using ClaudeCodeOrchestrator.App.ViewModels.Docking;

namespace ClaudeCodeOrchestrator.App.Views.Panels;

public partial class JobsView : UserControl
{
    private CancellationTokenSource? _previewCts;
    private readonly object _clickLock = new();
    private const int DoubleClickThresholdMs = 300;

    public JobsView()
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
        if (DataContext is not JobsViewModel vm) return;
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is not WorktreeViewModel worktree) return;

        // Cancel any pending preview operation (from previous selection or if double-click happens)
        CancellationTokenSource cts;
        lock (_clickLock)
        {
            _previewCts?.Cancel();
            _previewCts = cts = new CancellationTokenSource();
        }

        try
        {
            // Delay briefly to see if this is part of a double-click
            await Task.Delay(DoubleClickThresholdMs + 50, cts.Token);

            // If we got here without cancellation, this was a single-click - open preview
            vm.SelectWorktreeCommand.Execute(worktree);
        }
        catch (OperationCanceledException)
        {
            // Double-click happened or another selection occurred - don't open preview
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not JobsViewModel vm) return;

        // Cancel any pending preview operation
        lock (_clickLock)
        {
            _previewCts?.Cancel();
            _previewCts = null;
        }

        if (vm.SelectedWorktree is WorktreeViewModel worktree)
        {
            // Double-click opens as persistent (non-preview)
            vm.OpenWorktreeCommand.Execute(worktree);
        }
    }
}
