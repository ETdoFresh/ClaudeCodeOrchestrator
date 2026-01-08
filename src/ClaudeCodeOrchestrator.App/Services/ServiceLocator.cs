using Microsoft.Extensions.DependencyInjection;

namespace ClaudeCodeOrchestrator.App.Services;

/// <summary>
/// Static service locator for accessing services from ViewModels
/// that are created outside the DI container.
/// </summary>
public static class ServiceLocator
{
    private static IServiceProvider? _services;

    /// <summary>
    /// Gets the service provider instance.
    /// </summary>
    public static IServiceProvider? Services => _services;

    /// <summary>
    /// Initializes the service locator with the given service provider.
    /// </summary>
    public static void Initialize(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Gets a required service of the specified type.
    /// </summary>
    public static T GetRequiredService<T>() where T : notnull
    {
        if (_services is null)
            throw new InvalidOperationException("ServiceLocator has not been initialized. Call Initialize() first.");

        return _services.GetRequiredService<T>();
    }

    /// <summary>
    /// Gets an optional service of the specified type.
    /// </summary>
    public static T? GetService<T>() where T : class
    {
        return _services?.GetService<T>();
    }
}
