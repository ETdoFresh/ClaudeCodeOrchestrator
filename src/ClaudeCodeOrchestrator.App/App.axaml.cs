using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ClaudeCodeOrchestrator.App.Automation;
using ClaudeCodeOrchestrator.App.Services;
using ClaudeCodeOrchestrator.Core.Services;
using ClaudeCodeOrchestrator.Git.Services;

namespace ClaudeCodeOrchestrator.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private AutomationServer? _automationServer;

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

            // Start automation server for CLI integration testing
            _automationServer = new AutomationServer(new AutomationExecutor());
            _automationServer.Start();

            desktop.ShutdownRequested += (_, _) =>
            {
                // Dispose automation server
                _automationServer?.Dispose();

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

        // HTTP client for API calls
        services.AddSingleton<HttpClient>();

        // Title generator service (with API key resolution fallback chain)
        services.AddSingleton<ITitleGeneratorService>(sp =>
        {
            var settingsService = sp.GetRequiredService<ISettingsService>();
            var apiKey = ApiKeyResolver.ResolveOpenRouterApiKey(settingsService);
            return new TitleGeneratorService(sp.GetRequiredService<HttpClient>(), apiKey);
        });

        // Dialog service
        services.AddSingleton<IDialogService, DialogService>();

        // Git services
        services.AddSingleton<IGitService, GitService>();
        services.AddSingleton<IWorktreeService>(sp =>
            new WorktreeService(sp.GetRequiredService<IGitService>()));

        // Core services
        services.AddSingleton<ISessionService, SessionService>();

        // Repository settings service (per-repo configuration)
        services.AddSingleton<IRepositorySettingsService, RepositorySettingsService>();
    }
}
