using Avalonia;
using System;
using ClaudeCodeOrchestrator.App.Automation;

namespace ClaudeCodeOrchestrator.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // Check for automation CLI mode
        if (AutomationCli.IsAutomationMode(args))
        {
            return AutomationCli.RunAsync(args).GetAwaiter().GetResult();
        }

        // Normal GUI mode
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
