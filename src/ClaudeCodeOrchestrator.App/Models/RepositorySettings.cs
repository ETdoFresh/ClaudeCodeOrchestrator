namespace ClaudeCodeOrchestrator.App.Models;

/// <summary>
/// Per-repository settings stored in the repository root.
/// </summary>
public class RepositorySettings
{
    /// <summary>
    /// The executable to run when the play button is clicked.
    /// Can be:
    /// - An executable path with optional arguments (e.g., "dotnet run", "/path/to/app --arg")
    /// - A file path to open with the OS default handler (e.g., "index.html")
    /// </summary>
    public string? Executable { get; set; }
}
