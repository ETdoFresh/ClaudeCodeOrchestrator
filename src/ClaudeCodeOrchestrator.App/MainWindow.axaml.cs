using Avalonia.Controls;
using ClaudeCodeOrchestrator.App.ViewModels;
using ClaudeCodeOrchestrator.App.Views.Docking;

namespace ClaudeCodeOrchestrator.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Create and set view model
        var viewModel = new MainWindowViewModel();
        DataContext = viewModel;

        // Create dock factory and layout
        var factory = new DockFactory(viewModel);
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);

        viewModel.Layout = layout;
    }
}
