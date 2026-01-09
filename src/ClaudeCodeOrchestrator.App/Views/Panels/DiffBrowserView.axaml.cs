using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ClaudeCodeOrchestrator.App.ViewModels.Docking;

namespace ClaudeCodeOrchestrator.App.Views.Panels;

public partial class DiffBrowserView : UserControl
{
    public DiffBrowserView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Subscribe to selection changed for single-click (preview)
        DiffTree.SelectionChanged += OnSelectionChanged;

        // Subscribe to double-click for persistent open
        DiffTree.DoubleTapped += OnDoubleTapped;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        DiffTree.SelectionChanged -= OnSelectionChanged;
        DiffTree.DoubleTapped -= OnDoubleTapped;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not DiffBrowserViewModel vm) return;
        if (e.AddedItems.Count == 0) return;

        if (e.AddedItems[0] is DiffFileItemViewModel item && !item.IsDirectory)
        {
            vm.SelectDiffFileCommand.Execute(item);
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not DiffBrowserViewModel vm) return;

        if (vm.SelectedItem is DiffFileItemViewModel item && !item.IsDirectory)
        {
            vm.OpenDiffFileCommand.Execute(item);
        }
    }
}
