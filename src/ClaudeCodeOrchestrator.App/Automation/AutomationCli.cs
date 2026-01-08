namespace ClaudeCodeOrchestrator.App.Automation;

/// <summary>
/// Handles CLI arguments for automation commands.
/// </summary>
public static class AutomationCli
{
    /// <summary>
    /// Checks if the arguments indicate CLI automation mode.
    /// </summary>
    public static bool IsAutomationMode(string[] args)
    {
        return args.Length > 0 && args[0].StartsWith("--");
    }

    /// <summary>
    /// Runs automation CLI mode and returns the exit code.
    /// </summary>
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0].ToLower();

        try
        {
            var result = command switch
            {
                "--click" => await HandleClickAsync(args),
                "--type" => await HandleTypeAsync(args),
                "--key" => await HandleKeyAsync(args),
                "--screenshot" => await HandleScreenshotAsync(args),
                "--elements" => await HandleElementsAsync(args),
                "--wait" => await HandleWaitAsync(args),
                "--focus" => await HandleFocusAsync(args),
                "--ping" => await HandlePingAsync(),
                "--help" or "-h" => ShowHelp(),
                _ => AutomationResponse.Fail($"Unknown command: {command}")
            };

            if (result.Success)
            {
                if (!string.IsNullOrEmpty(result.Data))
                    Console.WriteLine(result.Data);
                return 0;
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<AutomationResponse> HandleClickAsync(string[] args)
    {
        var cmd = new ClickCommand();

        for (int i = 1; i < args.Length; i++)
        {
            var arg = args[i].ToLower();
            if (arg == "--id" && i + 1 < args.Length)
                cmd.AutomationId = args[++i];
            else if (arg == "--x" && i + 1 < args.Length)
                cmd.X = int.Parse(args[++i]);
            else if (arg == "--y" && i + 1 < args.Length)
                cmd.Y = int.Parse(args[++i]);
            else if (arg == "--double")
                cmd.DoubleClick = true;
            else if (!arg.StartsWith("--"))
                cmd.AutomationId = args[i]; // Shorthand: --click MyButton
        }

        return await AutomationClient.SendCommandAsync(cmd);
    }

    private static async Task<AutomationResponse> HandleTypeAsync(string[] args)
    {
        var cmd = new TypeTextCommand();

        for (int i = 1; i < args.Length; i++)
        {
            var arg = args[i].ToLower();
            if (arg == "--id" && i + 1 < args.Length)
                cmd.AutomationId = args[++i];
            else if (arg == "--text" && i + 1 < args.Length)
                cmd.Text = args[++i];
            else if (!arg.StartsWith("--"))
                cmd.Text = args[i]; // Shorthand: --type "Hello"
        }

        if (string.IsNullOrEmpty(cmd.Text))
            return AutomationResponse.Fail("Text required. Usage: --type \"text to type\"");

        return await AutomationClient.SendCommandAsync(cmd);
    }

    private static async Task<AutomationResponse> HandleKeyAsync(string[] args)
    {
        var cmd = new PressKeyCommand();

        for (int i = 1; i < args.Length; i++)
        {
            var arg = args[i].ToLower();
            if (arg == "--mod" && i + 1 < args.Length)
                cmd.Modifiers = args[++i];
            else if (!arg.StartsWith("--"))
                cmd.Key = args[i]; // Shorthand: --key Enter
        }

        // Parse combined format like "Ctrl+S"
        if (cmd.Key.Contains('+') && string.IsNullOrEmpty(cmd.Modifiers))
        {
            var parts = cmd.Key.Split('+');
            cmd.Key = parts[^1];
            cmd.Modifiers = string.Join("+", parts[..^1]);
        }

        if (string.IsNullOrEmpty(cmd.Key))
            return AutomationResponse.Fail("Key required. Usage: --key Enter or --key Ctrl+S");

        return await AutomationClient.SendCommandAsync(cmd);
    }

    private static async Task<AutomationResponse> HandleScreenshotAsync(string[] args)
    {
        var cmd = new ScreenshotCommand();

        for (int i = 1; i < args.Length; i++)
        {
            var arg = args[i].ToLower();
            if (arg == "--id" && i + 1 < args.Length)
                cmd.AutomationId = args[++i];
            else if (arg == "--output" || arg == "-o" && i + 1 < args.Length)
                cmd.OutputPath = args[++i];
            else if (!arg.StartsWith("--"))
                cmd.OutputPath = args[i]; // Shorthand: --screenshot output.png
        }

        return await AutomationClient.SendCommandAsync(cmd);
    }

    private static async Task<AutomationResponse> HandleElementsAsync(string[] args)
    {
        var cmd = new GetElementsCommand();

        for (int i = 1; i < args.Length; i++)
        {
            var arg = args[i].ToLower();
            if (arg == "--type" && i + 1 < args.Length)
                cmd.TypeFilter = args[++i];
            else if (arg == "--all")
                cmd.IncludeUnnamed = true;
            else if (!arg.StartsWith("--"))
                cmd.TypeFilter = args[i]; // Shorthand: --elements Button
        }

        return await AutomationClient.SendCommandAsync(cmd);
    }

    private static async Task<AutomationResponse> HandleWaitAsync(string[] args)
    {
        var cmd = new WaitCommand();

        for (int i = 1; i < args.Length; i++)
        {
            var arg = args[i].ToLower();
            if (arg == "--for" && i + 1 < args.Length)
                cmd.ForElement = args[++i];
            else if (arg == "--timeout" && i + 1 < args.Length)
                cmd.Timeout = int.Parse(args[++i]);
            else if (arg == "--ms" && i + 1 < args.Length)
                cmd.Milliseconds = int.Parse(args[++i]);
            else if (!arg.StartsWith("--") && int.TryParse(args[i], out var ms))
                cmd.Milliseconds = ms; // Shorthand: --wait 1000
        }

        return await AutomationClient.SendCommandAsync(cmd);
    }

    private static async Task<AutomationResponse> HandleFocusAsync(string[] args)
    {
        var cmd = new FocusCommand();

        for (int i = 1; i < args.Length; i++)
        {
            var arg = args[i].ToLower();
            if (!arg.StartsWith("--"))
                cmd.AutomationId = args[i]; // Shorthand: --focus MyTextBox
        }

        return await AutomationClient.SendCommandAsync(cmd);
    }

    private static async Task<AutomationResponse> HandlePingAsync()
    {
        var isRunning = await AutomationClient.IsAppRunningAsync();
        return isRunning
            ? AutomationResponse.Ok("Application is running")
            : AutomationResponse.Fail("Application is not running");
    }

    private static AutomationResponse ShowHelp()
    {
        PrintUsage();
        return AutomationResponse.Ok();
    }

    private static void PrintUsage()
    {
        Console.WriteLine(@"
Claude Code Orchestrator - Automation CLI

USAGE:
    ClaudeCodeOrchestrator.App [--command] [options]

COMMANDS:
    --click [id]              Click an element by automation ID
        --id <id>             Automation ID of element to click
        --x <x> --y <y>       Click at absolute coordinates
        --double              Double-click

    --type <text>             Type text into focused element
        --id <id>             Focus element first by automation ID
        --text <text>         Text to type

    --key <key>               Press a key or key combination
        Examples: --key Enter, --key Ctrl+S, --key Tab
        --mod <modifiers>     Modifiers (Ctrl, Alt, Shift, Meta)

    --screenshot [path]       Take a screenshot
        --output <path>       Save to file (otherwise returns base64)
        --id <id>             Screenshot specific element

    --elements [type]         List automation elements
        --type <type>         Filter by element type (Button, TextBox, etc.)
        --all                 Include elements without automation IDs

    --wait <ms>               Wait for duration or element
        --ms <ms>             Milliseconds to wait
        --for <id>            Wait for element to appear
        --timeout <ms>        Max wait time for element (default 5000)

    --focus [id]              Focus an element or the main window

    --ping                    Check if application is running

    --help, -h                Show this help

EXAMPLES:
    # Click the Open Repository button
    ClaudeCodeOrchestrator.App --click OpenRepositoryButton

    # Type text into a text box
    ClaudeCodeOrchestrator.App --type ""Hello World"" --id TaskDescriptionInput

    # Press Ctrl+S to save
    ClaudeCodeOrchestrator.App --key Ctrl+S

    # Take a screenshot
    ClaudeCodeOrchestrator.App --screenshot ./screenshot.png

    # List all buttons
    ClaudeCodeOrchestrator.App --elements Button

    # Wait for a dialog to appear
    ClaudeCodeOrchestrator.App --wait --for NewTaskDialog --timeout 10000
");
    }
}
