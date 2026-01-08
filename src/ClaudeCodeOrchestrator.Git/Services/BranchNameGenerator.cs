using System.Text.RegularExpressions;

namespace ClaudeCodeOrchestrator.Git.Services;

/// <summary>
/// Generates branch names from task descriptions.
/// </summary>
public sealed partial class BranchNameGenerator
{
    private const int MaxSlugLength = 50;
    private const string BranchPrefix = "task/";

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex InvalidCharsRegex();

    [GeneratedRegex(@"-+")]
    private static partial Regex MultipleHyphensRegex();

    /// <summary>
    /// Generates a branch name from a task description.
    /// </summary>
    /// <param name="taskDescription">The task description to convert.</param>
    /// <returns>A valid Git branch name.</returns>
    public string Generate(string taskDescription)
    {
        if (string.IsNullOrWhiteSpace(taskDescription))
        {
            taskDescription = "untitled";
        }

        // Convert to lowercase and replace spaces with hyphens
        var slug = taskDescription.ToLowerInvariant().Replace(' ', '-');

        // Remove invalid characters
        slug = InvalidCharsRegex().Replace(slug, "");

        // Collapse multiple hyphens
        slug = MultipleHyphensRegex().Replace(slug, "-");

        // Trim hyphens from ends
        slug = slug.Trim('-');

        // Truncate to max length
        if (slug.Length > MaxSlugLength)
        {
            slug = slug[..MaxSlugLength].TrimEnd('-');
        }

        // Handle empty result
        if (string.IsNullOrEmpty(slug))
        {
            slug = "task";
        }

        // Add timestamp for uniqueness
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

        return $"{BranchPrefix}{slug}-{timestamp}";
    }

    /// <summary>
    /// Extracts the task slug from a branch name.
    /// </summary>
    public string? ExtractSlug(string branchName)
    {
        if (!branchName.StartsWith(BranchPrefix))
        {
            return null;
        }

        var withoutPrefix = branchName[BranchPrefix.Length..];

        // Remove timestamp suffix (format: -YYYYMMDD-HHMMSS)
        var lastHyphen = withoutPrefix.LastIndexOf('-');
        if (lastHyphen > 0)
        {
            var secondLastHyphen = withoutPrefix.LastIndexOf('-', lastHyphen - 1);
            if (secondLastHyphen > 0)
            {
                return withoutPrefix[..secondLastHyphen];
            }
        }

        return withoutPrefix;
    }
}
