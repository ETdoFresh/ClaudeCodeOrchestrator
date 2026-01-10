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

            await client.ConnectAsync(TimeoutMs);

            using var reader = new StreamReader(client, Encoding.UTF8);
            await using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };

            await writer.WriteLineAsync(command.ToJson());
            var response = await reader.ReadLineAsync();

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
