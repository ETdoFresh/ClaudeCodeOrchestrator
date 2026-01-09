using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClaudeCodeOrchestrator.SDK;
using ClaudeCodeOrchestrator.SDK.Messages;
using ClaudeCodeOrchestrator.SDK.Options;

namespace ClaudeCodeOrchestrator.Core.Services;

/// <summary>
/// Service for generating titles and branch names from prompts using an LLM API.
/// Falls back to local extraction if API is unavailable.
/// </summary>
public sealed partial class TitleGeneratorService : ITitleGeneratorService
{
    private const string DefaultBranchPrefix = "task/";
    private const int MaxSlugLength = 50;

    private readonly HttpClient _httpClient;
    private readonly string? _openRouterApiKey;

    // JSON prompt for LLM-based title generation
    private const string JsonPromptTemplate = """
        Generate a title and git branch name that SUMMARIZE the core intent of this coding request. Do NOT just use the first few words - analyze the full request and create a concise summary.

        Respond with ONLY valid JSON, no other text:
        {"title": "Concise summary title", "branch_name": "task/summarized-intent"}

        Rules:
        - Title: 3-6 words that capture the MAIN PURPOSE of the request
        - Branch: starts with "task/", kebab-case, 2-4 words summarizing the key action, NO timestamp
        - Title and branch should express the same concept (branch is kebab-case version of title idea)
        - Focus on WHAT is being done, not HOW the user phrased it

        Examples:
        - "I want to update the prompt that generates..." → {"title": "Improve Title Generation Prompt", "branch_name": "task/improve-title-generation"}
        - "Can you help me fix the bug where..." → {"title": "Fix Authentication Bug", "branch_name": "task/fix-auth-bug"}
        - "Please add a new feature for..." → {"title": "Add Export Feature", "branch_name": "task/add-export-feature"}

        Request: "{{PROMPT}}"
        """;

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex InvalidCharsRegex();

    [GeneratedRegex(@"-+")]
    private static partial Regex MultipleHyphensRegex();

    // Common filler words to remove for better local summarization
    private static readonly HashSet<string> FillerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "i", "want", "to", "the", "a", "an", "please", "can", "you", "help", "me",
        "with", "for", "this", "that", "it", "is", "be", "are", "was", "were",
        "have", "has", "had", "do", "does", "did", "will", "would", "could", "should",
        "may", "might", "must", "shall", "need", "just", "also", "so", "if", "when",
        "where", "how", "what", "which", "who", "why", "all", "each", "every", "both",
        "few", "more", "most", "other", "some", "such", "no", "not", "only", "same",
        "than", "too", "very", "but", "and", "or", "as", "at", "by", "from",
        "in", "into", "of", "on", "out", "over", "through", "under", "up", "down",
        "about", "after", "before", "between", "during", "like", "make", "sure"
    };

    public TitleGeneratorService(HttpClient httpClient, string? openRouterApiKey = null)
    {
        _httpClient = httpClient;
        _openRouterApiKey = openRouterApiKey ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
    }

    /// <inheritdoc />
    public async Task<GeneratedTitle> GenerateTitleAsync(string prompt, CancellationToken cancellationToken = default)
    {
        // Try OpenRouter API if key is available (fastest)
        if (!string.IsNullOrEmpty(_openRouterApiKey))
        {
            var apiResult = await TryOpenRouterAsync(prompt, cancellationToken);
            if (apiResult != null)
            {
                return apiResult;
            }
        }

        // Fall back to Claude SDK (uses local Claude Code installation)
        var claudeResult = await TryClaudeSdkAsync(prompt, cancellationToken);
        if (claudeResult != null)
        {
            return claudeResult;
        }

        // Final fallback to local generation
        return GenerateTitleSync(prompt);
    }

    /// <inheritdoc />
    public GeneratedTitle GenerateTitleSync(string prompt)
    {
        var result = GenerateTitleLocal(prompt);
        return new GeneratedTitle
        {
            Title = result.Title,
            BranchName = result.BranchName,
            Source = "fallback"
        };
    }

    /// <summary>
    /// Try OpenRouter API for title generation.
    /// </summary>
    private async Task<GeneratedTitle?> TryOpenRouterAsync(string prompt, CancellationToken cancellationToken)
    {
        try
        {
            var titlePrompt = JsonPromptTemplate.Replace("{{PROMPT}}", prompt);

            var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
            {
                Content = JsonContent.Create(new
                {
                    model = "google/gemini-3-flash-preview",
                    messages = new[]
                    {
                        new { role = "user", content = titlePrompt }
                    },
                    max_tokens = 100
                })
            };
            request.Headers.Add("Authorization", $"Bearer {_openRouterApiKey}");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var data = await response.Content.ReadFromJsonAsync<OpenRouterResponse>(cancellationToken: cancellationToken);
            var content = data?.Choices?.FirstOrDefault()?.Message?.Content;

            if (!string.IsNullOrEmpty(content))
            {
                var parsed = ParseJsonResponse(content);
                if (parsed != null)
                {
                    return new GeneratedTitle
                    {
                        Title = parsed.Value.Title,
                        BranchName = EnsureBranchFormat(parsed.Value.BranchName),
                        Source = "api"
                    };
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Try Claude SDK for title generation (uses local Claude Code installation).
    /// </summary>
    private async Task<GeneratedTitle?> TryClaudeSdkAsync(string prompt, CancellationToken cancellationToken)
    {
        try
        {
            var titlePrompt = JsonPromptTemplate.Replace("{{PROMPT}}", prompt);

            var options = new ClaudeAgentOptions
            {
                Model = "claude-haiku-4-20250514",
                MaxTurns = 1,
                PermissionMode = PermissionMode.Plan // No tool use needed
            };

            string? responseContent = null;

            await foreach (var message in ClaudeAgent.QueryAsync(titlePrompt, options, cancellationToken))
            {
                if (message is SDKAssistantMessage assistantMsg)
                {
                    // Extract text from content blocks
                    foreach (var block in assistantMsg.Message.Content)
                    {
                        if (block is TextContentBlock textBlock)
                        {
                            responseContent = textBlock.Text;
                            break;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(responseContent))
            {
                var parsed = ParseJsonResponse(responseContent);
                if (parsed != null)
                {
                    return new GeneratedTitle
                    {
                        Title = parsed.Value.Title,
                        BranchName = EnsureBranchFormat(parsed.Value.BranchName),
                        Source = "claude"
                    };
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parse JSON response from LLM.
    /// </summary>
    private static (string Title, string BranchName)? ParseJsonResponse(string content)
    {
        try
        {
            // Find JSON in response (in case there's extra text)
            var jsonMatch = Regex.Match(content, @"\{[\s\S]*?""title""[\s\S]*?""branch_name""[\s\S]*?\}");
            if (!jsonMatch.Success)
            {
                return null;
            }

            var doc = JsonDocument.Parse(jsonMatch.Value);
            var title = doc.RootElement.GetProperty("title").GetString();
            var branchName = doc.RootElement.GetProperty("branch_name").GetString();

            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(branchName))
            {
                return (title, branchName);
            }
        }
        catch
        {
            // JSON parsing failed
        }

        return null;
    }

    /// <summary>
    /// Local fallback - extract key action words from prompt.
    /// </summary>
    private static (string Title, string BranchName) GenerateTitleLocal(string prompt)
    {
        // Clean the prompt
        var cleaned = Regex.Replace(prompt, @"\n", " ");
        cleaned = Regex.Replace(cleaned, @"[^a-zA-Z0-9\s]", " ");
        cleaned = cleaned.ToLowerInvariant().Trim();

        // Extract meaningful words (non-filler words)
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 2 && !FillerWords.Contains(word))
            .ToList();

        // Take up to 5 meaningful words for the title
        var titleWords = words.Take(5).ToList();

        // If we didn't get enough meaningful words, use original approach
        if (titleWords.Count < 2)
        {
            var fallbackWords = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5).ToList();
            var title = string.Join(" ", fallbackWords.Select(ToTitleCase));
            if (string.IsNullOrEmpty(title))
            {
                title = "New Session";
            }
            return (title, GenerateBranchFromTitle(title));
        }

        // Convert to Title Case
        var resultTitle = string.Join(" ", titleWords.Select(ToTitleCase));
        return (resultTitle, GenerateBranchFromTitle(resultTitle));
    }

    /// <summary>
    /// Generate branch name from title.
    /// </summary>
    private static string GenerateBranchFromTitle(string title)
    {
        var slug = title.ToLowerInvariant()
            .Replace(' ', '-');

        slug = InvalidCharsRegex().Replace(slug, "");
        slug = MultipleHyphensRegex().Replace(slug, "-");
        slug = slug.Trim('-');

        if (slug.Length > MaxSlugLength)
        {
            slug = slug[..MaxSlugLength].TrimEnd('-');
        }

        if (string.IsNullOrEmpty(slug))
        {
            slug = "session";
        }

        return $"{DefaultBranchPrefix}{slug}";
    }

    /// <summary>
    /// Ensure branch name has correct format.
    /// </summary>
    private static string EnsureBranchFormat(string branchName)
    {
        // Remove any timestamp suffix that might have been added
        var branch = branchName;

        // Ensure it starts with task/
        if (!branch.StartsWith(DefaultBranchPrefix))
        {
            branch = DefaultBranchPrefix + branch.TrimStart('/');
        }

        // Clean up the branch name
        var slug = branch[DefaultBranchPrefix.Length..];
        slug = InvalidCharsRegex().Replace(slug.ToLowerInvariant(), "");
        slug = MultipleHyphensRegex().Replace(slug, "-");
        slug = slug.Trim('-');

        if (slug.Length > MaxSlugLength)
        {
            slug = slug[..MaxSlugLength].TrimEnd('-');
        }

        if (string.IsNullOrEmpty(slug))
        {
            slug = "session";
        }

        return $"{DefaultBranchPrefix}{slug}";
    }

    private static string ToTitleCase(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        return char.ToUpperInvariant(word[0]) + word[1..];
    }

    // OpenRouter response types
    private sealed class OpenRouterResponse
    {
        public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        public Message? Message { get; set; }
    }

    private sealed class Message
    {
        public string? Content { get; set; }
    }
}
