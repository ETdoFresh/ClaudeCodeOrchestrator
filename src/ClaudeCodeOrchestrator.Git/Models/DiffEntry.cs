namespace ClaudeCodeOrchestrator.Git.Models;

/// <summary>
/// Represents a file change in a diff.
/// </summary>
public sealed record DiffEntry
{
    /// <summary>
    /// Path to the file.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Type of change.
    /// </summary>
    public required DiffChangeType ChangeType { get; init; }

    /// <summary>
    /// Old path (for renames/copies).
    /// </summary>
    public string? OldPath { get; init; }

    /// <summary>
    /// Number of lines added.
    /// </summary>
    public int LinesAdded { get; init; }

    /// <summary>
    /// Number of lines deleted.
    /// </summary>
    public int LinesDeleted { get; init; }

    /// <summary>
    /// The patch content (unified diff format).
    /// </summary>
    public string? Patch { get; init; }
}

/// <summary>
/// Type of change in a diff.
/// </summary>
public enum DiffChangeType
{
    Added,
    Deleted,
    Modified,
    Renamed,
    Copied,
    TypeChanged,
    Unmodified
}
