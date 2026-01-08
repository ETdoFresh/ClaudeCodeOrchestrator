using Avalonia.Controls;
using ClaudeCodeOrchestrator.App.ViewModels;
using ClaudeCodeOrchestrator.App.Views.Docking;

namespace ClaudeCodeOrchestrator.App;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        // Create and set view model
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        // Create dock factory and layout
        var factory = new DockFactory(_viewModel);
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);

        // Connect factory and layout to view model
        _viewModel.Factory = factory;
        _viewModel.Layout = layout;

        // Handle window closing for cleanup
        Closing += OnWindowClosing;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Dispose view model to unsubscribe from events
        _viewModel?.Dispose();
    }
}
