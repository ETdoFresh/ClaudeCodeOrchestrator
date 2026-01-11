using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace ClaudeCodeOrchestrator.App.Automation;

/// <summary>
/// Named pipe server for receiving automation commands from CLI.
/// </summary>
public class AutomationServer : IDisposable
{
    private const string PipeNamePrefix = "ClaudeCodeOrchestrator.Automation";

    /// <summary>
    /// Gets the pipe name for the current process.
    /// </summary>
    public static string PipeName => GetPipeName(Process.GetCurrentProcess().Id);

    /// <summary>
    /// Gets the pipe name for a specific process ID.
    /// </summary>
    public static string GetPipeName(int pid) => $"{PipeNamePrefix}.{pid}";

    private readonly AutomationExecutor _executor;
    private readonly CancellationTokenSource _cts = new();
    private Task? _serverTask;
    private bool _disposed;

    public AutomationServer(AutomationExecutor executor)
    {
        _executor = executor;
    }

    public void Start()
    {
        _serverTask = Task.Run(ServerLoopAsync);
    }

    private async Task ServerLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(_cts.Token);

                // Handle client synchronously to avoid pipe disposal issues
                await HandleClientAsync(server);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Automation server error: {ex.Message}");
                await Task.Delay(1000, _cts.Token);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server)
    {
        try
        {
            await using (server)
            {
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line))
                {
                    await writer.WriteLineAsync(AutomationResponse.Fail("Empty command").ToJson());
                    await writer.FlushAsync();
                    return;
                }

                var command = AutomationCommand.Parse(line);
                if (command is null)
                {
                    await writer.WriteLineAsync(AutomationResponse.Fail("Invalid command JSON").ToJson());
                    await writer.FlushAsync();
                    return;
                }

                AutomationResponse response;
                try
                {
                    // Add timeout for command execution to prevent hanging
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                    var executeTask = _executor.ExecuteAsync(command);
                    var completedTask = await Task.WhenAny(executeTask, Task.Delay(Timeout.Infinite, cts.Token));

                    if (completedTask == executeTask)
                    {
                        response = await executeTask;
                    }
                    else
                    {
                        response = AutomationResponse.Fail("Command execution timed out");
                    }
                }
                catch (Exception ex)
                {
                    response = AutomationResponse.Fail($"Command execution error: {ex.Message}");
                }

                await writer.WriteLineAsync(response.ToJson());
                await writer.FlushAsync();
                await server.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error handling automation client: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _cts.Dispose();
    }
}
