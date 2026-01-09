using Avalonia.Controls;
using Avalonia.Threading;
using ClaudeCodeOrchestrator.App.Services;
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

        // Get settings service
        var settingsService = ServiceLocator.GetRequiredService<ISettingsService>();

        // Create dock factory and layout
        var factory = new DockFactory(_viewModel, settingsService);
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);

        // Connect factory and layout to view model
        _viewModel.Factory = factory;
        _viewModel.Layout = layout;

        // Handle window closing for cleanup
        Closing += OnWindowClosing;

        // Initialize after window is loaded (restore last repository)
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Fire and forget - restore last repository asynchronously
        Dispatcher.UIThread.Post(async () =>
        {
            await (_viewModel?.InitializeAsync() ?? Task.CompletedTask);
        });
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Dispose view model to unsubscribe from events
        _viewModel?.Dispose();
    }
}
