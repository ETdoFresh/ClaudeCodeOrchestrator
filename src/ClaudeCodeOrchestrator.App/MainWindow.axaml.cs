using Avalonia.Controls;
using Avalonia.Input;
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

        // Get services
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

        // Set up drag-drop handlers for opening repositories
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
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
        // Close all documents (disposes session documents, file documents, etc.)
        _viewModel?.Factory?.CloseAllDocuments();

        // Clear worktrees (stops timers, releases resources)
        _viewModel?.Factory?.ClearWorktrees();

        // Dispose view model to unsubscribe from events
        _viewModel?.Dispose();

        // Note: SessionService.Dispose() is called via _serviceProvider.Dispose() in App.axaml.cs
        // which ends all sessions and kills all Claude Code subprocesses
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // Check if this is a folder being dragged
        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            if (files != null)
            {
                foreach (var item in files)
                {
                    // Check if it's a directory
                    if (item is Avalonia.Platform.Storage.IStorageFolder)
                    {
                        e.DragEffects = DragDropEffects.Copy;
                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        // Not a valid folder drop
        e.DragEffects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            if (files != null)
            {
                foreach (var item in files)
                {
                    // Check if it's a directory
                    if (item is Avalonia.Platform.Storage.IStorageFolder folder)
                    {
                        var path = folder.Path.LocalPath;
                        if (!string.IsNullOrEmpty(path))
                        {
                            // Open the repository asynchronously
                            Dispatcher.UIThread.Post(async () =>
                            {
                                await (_viewModel?.OpenRepositoryAtPathAsync(path) ?? Task.CompletedTask);
                            });
                            e.Handled = true;
                            return;
                        }
                    }
                }
            }
        }
    }
}
