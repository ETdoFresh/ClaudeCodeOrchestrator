using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ClaudeCodeOrchestrator.App.Models;
using ClaudeCodeOrchestrator.App.ViewModels;
using ClaudeCodeOrchestrator.App.ViewModels.Docking;
using ClaudeCodeOrchestrator.Git.Models;

namespace ClaudeCodeOrchestrator.App.Views.Panels;

public partial class WorktreesView : UserControl
{
    public static FuncValueConverter<WorktreeStatus, IBrush> StatusToBrushConverter { get; } =
        new(status => status switch
        {
            WorktreeStatus.Active => new SolidColorBrush(Color.Parse("#0E639C")),      // Blue - Active
            WorktreeStatus.HasChanges => new SolidColorBrush(Color.Parse("#0E639C")),  // Blue - Active (same as Active)
            WorktreeStatus.ReadyToMerge => new SolidColorBrush(Color.Parse("#388A34")), // Green - Ready
            WorktreeStatus.Merged => new SolidColorBrush(Color.Parse("#4EC9B0")),      // Cyan - Complete
            WorktreeStatus.Locked => new SolidColorBrush(Color.Parse("#6C2022")),      // Red - Locked
            _ => new SolidColorBrush(Color.Parse("#666666"))
        });

    /// <summary>
    /// Converts DisplayStatus tuple color to brush.
    /// </summary>
    public static FuncValueConverter<(string Text, string Color), IBrush> DisplayStatusToBrushConverter { get; } =
        new(status => new SolidColorBrush(Color.Parse(status.Color)));

    public static FuncValueConverter<int, bool> PositiveNumberConverter { get; } =
        new(value => value > 0);

    private CancellationTokenSource? _previewCts;
    private readonly object _clickLock = new();
    private const int DoubleClickThresholdMs = 300;

    public WorktreesView()
    {
        InitializeComponent();

        // Subscribe to task creation event from inline input
        NewTaskInput.TaskCreationRequested += OnTaskCreationRequested;
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
        NewTaskInput.TaskCreationRequested -= OnTaskCreationRequested;
    }

    private async void OnTaskCreationRequested(object? sender, TaskInput taskInput)
    {
        if (DataContext is not WorktreesViewModel vm) return;

        NewTaskInput.IsCreating = true;
        NewTaskInput.ClearError();

        try
        {
            if (vm.OnCreateTaskWithInputRequested != null)
            {
                await vm.OnCreateTaskWithInputRequested(taskInput);
                NewTaskInput.Clear();
                NewTaskInput.FocusInput();
            }
        }
        catch (Exception ex)
        {
            NewTaskInput.ShowError(ex.Message);
        }
        finally
        {
            NewTaskInput.IsCreating = false;
        }
    }

    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not WorktreesViewModel vm) return;
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
        if (DataContext is not WorktreesViewModel vm) return;

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
