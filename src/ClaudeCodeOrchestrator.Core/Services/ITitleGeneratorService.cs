namespace ClaudeCodeOrchestrator.Core.Services;

/// <summary>
/// Result of title generation.
/// </summary>
public sealed record GeneratedTitle
{
    /// <summary>
    /// The generated title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// The generated branch name.
    /// </summary>
    public required string BranchName { get; init; }

    /// <summary>
    /// Source of the generation (api, fallback).
    /// </summary>
    public required string Source { get; init; }
}

/// <summary>
/// Service for generating titles and branch names from prompts.
/// Uses an LLM API to create concise, meaningful titles instead of truncating prompts.
/// </summary>
public interface ITitleGeneratorService
{
    /// <summary>
    /// Generates a title and branch name for a given prompt.
    /// </summary>
    /// <param name="prompt">The user's prompt to generate a title for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated title and branch name.</returns>
    Task<GeneratedTitle> GenerateTitleAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Quick synchronous title generation using local fallback only.
    /// Use this when you need immediate title without async API call.
    /// </summary>
    /// <param name="prompt">The user's prompt.</param>
    /// <returns>Generated title and branch name.</returns>
    GeneratedTitle GenerateTitleSync(string prompt);
}
