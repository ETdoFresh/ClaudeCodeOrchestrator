using System.IO.Pipes;
using System.Text;

namespace ClaudeCodeOrchestrator.App.Automation;

/// <summary>
/// Client for sending automation commands to a running app instance.
/// </summary>
public static class AutomationClient
{
    private const int TimeoutMs = 30000;

    /// <summary>
    /// Sends a command to the running application and returns the response.
    /// </summary>
    /// <param name="command">The automation command to send.</param>
    /// <param name="pid">The process ID of the target application.</param>
    public static async Task<AutomationResponse> SendCommandAsync(AutomationCommand command, int pid)
    {
        try
        {
            var pipeName = AutomationServer.GetPipeName(pid);
            await using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            // Connect with a shorter timeout, then use the full timeout for the operation
            await client.ConnectAsync(5000);

            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            // Write command
            await writer.WriteLineAsync(command.ToJson());
            await writer.FlushAsync();

            // Read response with timeout
            using var cts = new CancellationTokenSource(TimeoutMs);
            var readTask = reader.ReadLineAsync(cts.Token);

            string? response;
            try
            {
                response = await readTask;
            }
            catch (OperationCanceledException)
            {
                return AutomationResponse.Fail("Read timeout - server did not respond in time");
            }

            if (string.IsNullOrEmpty(response))
                return AutomationResponse.Fail("Empty response from server");

            return AutomationResponse.Parse(response) ?? AutomationResponse.Fail("Invalid response JSON");
        }
        catch (TimeoutException)
        {
            return AutomationResponse.Fail($"Connection timeout - is the application with PID {pid} running?");
        }
        catch (Exception ex)
        {
            return AutomationResponse.Fail($"Connection error: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if the application with the specified PID is running and accepting commands.
    /// </summary>
    /// <param name="pid">The process ID of the target application.</param>
    public static async Task<bool> IsAppRunningAsync(int pid)
    {
        try
        {
            var pipeName = AutomationServer.GetPipeName(pid);
            await using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await client.ConnectAsync(1000);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
