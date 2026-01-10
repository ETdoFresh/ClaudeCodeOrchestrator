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

    private DateTime _lastSelectionTime;
    private DateTime _lastDoubleClickTime;
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

        // Record selection time to detect double-clicks
        var selectionTime = DateTime.UtcNow;
        _lastSelectionTime = selectionTime;

        // Delay briefly to see if this is part of a double-click
        await Task.Delay(DoubleClickThresholdMs + 50); // Add buffer for timing variance

        // Check if a double-click happened during our delay
        // If _lastDoubleClickTime is more recent than our selection, skip preview
        if (_lastDoubleClickTime > selectionTime)
        {
            return;
        }

        // Also check if another selection happened (user clicked elsewhere)
        if (_lastSelectionTime != selectionTime)
        {
            return;
        }

        vm.SelectWorktreeCommand.Execute(worktree);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not WorktreesViewModel vm) return;

        // Record double-click time to signal pending selection handlers to abort
        _lastDoubleClickTime = DateTime.UtcNow;

        if (vm.SelectedWorktree is WorktreeViewModel worktree)
        {
            // Double-click opens as persistent (non-preview)
            vm.OpenWorktreeCommand.Execute(worktree);
        }
    }
}
