namespace ClaudeCodeOrchestrator.App.Services;

/// <summary>
/// Abstraction for UI thread dispatcher to enable thread-safe UI updates.
/// </summary>
public interface IDispatcher
{
    /// <summary>
    /// Posts an action to be executed on the UI thread asynchronously.
    /// </summary>
    void Post(Action action);

    /// <summary>
    /// Invokes an action on the UI thread and waits for completion.
    /// </summary>
    Task InvokeAsync(Action action);

    /// <summary>
    /// Invokes a function on the UI thread and returns the result.
    /// </summary>
    Task<T> InvokeAsync<T>(Func<T> func);

    /// <summary>
    /// Checks if the current thread has access to the UI thread.
    /// </summary>
    bool CheckAccess();
}
