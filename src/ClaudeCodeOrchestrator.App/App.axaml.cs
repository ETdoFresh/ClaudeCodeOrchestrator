using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ClaudeCodeOrchestrator.App.Services;
using ClaudeCodeOrchestrator.Core.Services;
using ClaudeCodeOrchestrator.Git.Services;

namespace ClaudeCodeOrchestrator.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Build service collection
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Initialize service locator for ViewModels
        ServiceLocator.Initialize(_serviceProvider);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();

            desktop.ShutdownRequested += (_, _) =>
            {
                // Dispose services on shutdown
                _serviceProvider?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // UI dispatcher for thread-safe updates
        services.AddSingleton<IDispatcher, AvaloniaDispatcher>();

        // Settings service (load immediately)
        services.AddSingleton<ISettingsService>(sp =>
        {
            var settings = new SettingsService();
            settings.Load();
            return settings;
        });

        // Dialog service
        services.AddSingleton<IDialogService, DialogService>();

        // Git services
        services.AddSingleton<IGitService, GitService>();
        services.AddSingleton<BranchNameGenerator>();
        services.AddSingleton<IWorktreeService>(sp =>
            new WorktreeService(
                sp.GetRequiredService<IGitService>(),
                sp.GetRequiredService<BranchNameGenerator>()));

        // Core services
        services.AddSingleton<ISessionService, SessionService>();
    }
}
