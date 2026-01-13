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

    /// <summary>
    /// Saved custom prompts for the Jobs panel.
    /// </summary>
    public List<SavedPromptSettings>? SavedPrompts { get; set; }

    /// <summary>
    /// The branch prefix for task worktrees (default: "task/").
    /// </summary>
    public string? TaskBranchPrefix { get; set; }

    /// <summary>
    /// The branch prefix for job worktrees (default: "jobs/").
    /// </summary>
    public string? JobBranchPrefix { get; set; }
}

/// <summary>
/// A saved custom prompt for the Jobs panel.
/// </summary>
public class SavedPromptSettings
{
    /// <summary>
    /// The title/name of the saved prompt.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The prompt text.
    /// </summary>
    public string PromptText { get; set; } = string.Empty;
}
