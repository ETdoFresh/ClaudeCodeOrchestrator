using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ClaudeCodeOrchestrator.App.ViewModels.Docking;

namespace ClaudeCodeOrchestrator.App.Views.Panels;

public partial class FileBrowserView : UserControl
{
    public FileBrowserView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Subscribe to selection changed for single-click (preview)
        FileTree.SelectionChanged += OnSelectionChanged;

        // Subscribe to double-click for persistent open
        FileTree.DoubleTapped += OnDoubleTapped;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        FileTree.SelectionChanged -= OnSelectionChanged;
        FileTree.DoubleTapped -= OnDoubleTapped;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not FileBrowserViewModel vm) return;
        if (e.AddedItems.Count == 0) return;

        if (e.AddedItems[0] is FileItemViewModel item && !item.IsDirectory)
        {
            vm.SelectFileCommand.Execute(item);
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not FileBrowserViewModel vm) return;

        if (vm.SelectedItem is FileItemViewModel item && !item.IsDirectory)
        {
            vm.OpenFileCommand.Execute(item);
        }
    }
}
