using System.IO.Pipes;
using System.Text;

namespace ClaudeCodeOrchestrator.App.Automation;

/// <summary>
/// Named pipe server for receiving automation commands from CLI.
/// </summary>
public class AutomationServer : IDisposable
{
    public const string PipeName = "ClaudeCodeOrchestrator.Automation";

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
                    return;
                }

                var command = AutomationCommand.Parse(line);
                if (command is null)
                {
                    await writer.WriteLineAsync(AutomationResponse.Fail("Invalid command JSON").ToJson());
                    return;
                }

                var response = await _executor.ExecuteAsync(command);
                await writer.WriteLineAsync(response.ToJson());
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
