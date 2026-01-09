namespace ClaudeCodeOrchestrator.App.Models;

/// <summary>
/// Represents user input containing text and optional image attachments.
/// Used for task creation and session messages.
/// </summary>
public sealed class TaskInput
{
    /// <summary>
    /// The text content of the input.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Optional image attachments.
    /// </summary>
    public IReadOnlyList<ImageAttachment> Images { get; init; } = Array.Empty<ImageAttachment>();

    /// <summary>
    /// The generated title for the task (if available).
    /// </summary>
    public string? GeneratedTitle { get; init; }

    /// <summary>
    /// The generated branch name for the task (if available).
    /// </summary>
    public string? GeneratedBranch { get; init; }

    /// <summary>
    /// Whether this input has any images.
    /// </summary>
    public bool HasImages => Images.Count > 0;

    /// <summary>
    /// Creates a simple text-only input.
    /// </summary>
    public static TaskInput FromText(string text) => new() { Text = text };

    /// <summary>
    /// Creates an input with text, images, and optionally generated title/branch.
    /// </summary>
    public static TaskInput Create(
        string text,
        IReadOnlyList<ImageAttachment>? images = null,
        string? generatedTitle = null,
        string? generatedBranch = null)
    {
        return new TaskInput
        {
            Text = text,
            Images = images ?? Array.Empty<ImageAttachment>(),
            GeneratedTitle = generatedTitle,
            GeneratedBranch = generatedBranch
        };
    }
}
