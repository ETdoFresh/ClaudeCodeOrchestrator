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
    public static async Task<AutomationResponse> SendCommandAsync(AutomationCommand command)
    {
        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                AutomationServer.PipeName,
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
            return AutomationResponse.Fail("Connection timeout - is the application running?");
        }
        catch (Exception ex)
        {
            return AutomationResponse.Fail($"Connection error: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if the application is running and accepting commands.
    /// </summary>
    public static async Task<bool> IsAppRunningAsync()
    {
        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                AutomationServer.PipeName,
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
