using Avalonia.Threading;

namespace ClaudeCodeOrchestrator.App.Services;

/// <summary>
/// Avalonia implementation of the dispatcher interface.
/// </summary>
public sealed class AvaloniaDispatcher : IDispatcher
{
    /// <inheritdoc />
    public void Post(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }

    /// <inheritdoc />
    public Task InvokeAsync(Action action)
    {
        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }

    /// <inheritdoc />
    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        return Dispatcher.UIThread.InvokeAsync(func).GetTask();
    }

    /// <inheritdoc />
    public bool CheckAccess()
    {
        return Dispatcher.UIThread.CheckAccess();
    }
}
