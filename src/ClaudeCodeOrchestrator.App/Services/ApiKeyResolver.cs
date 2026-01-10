namespace ClaudeCodeOrchestrator.App.Services;

/// <summary>
/// Resolves API keys using a fallback chain: Settings → .env file → Environment variable.
/// </summary>
public static class ApiKeyResolver
{
    private const string OpenRouterApiKeyName = "OPENROUTER_API_KEY";

    /// <summary>
    /// Resolves the OpenRouter API key using the fallback chain:
    /// 1. Application settings (highest priority)
    /// 2. .env file in current directory or user home
    /// 3. System environment variable (lowest priority)
    /// </summary>
    /// <param name="settingsService">The settings service to check first.</param>
    /// <returns>The resolved API key, or null if not found.</returns>
    public static string? ResolveOpenRouterApiKey(ISettingsService? settingsService)
    {
        // 1. Check settings first (highest priority)
        var settingsKey = settingsService?.OpenRouterApiKey;
        if (!string.IsNullOrWhiteSpace(settingsKey))
        {
            return settingsKey;
        }

        // 2. Check .env file
        var envFileKey = ReadFromEnvFile(OpenRouterApiKeyName);
        if (!string.IsNullOrWhiteSpace(envFileKey))
        {
            return envFileKey;
        }

        // 3. Fall back to system environment variable
        return Environment.GetEnvironmentVariable(OpenRouterApiKeyName);
    }

    /// <summary>
    /// Reads a value from .env files, checking current directory first, then user home.
    /// </summary>
    private static string? ReadFromEnvFile(string key)
    {
        // Check current directory first
        var currentDirEnvPath = Path.Combine(Environment.CurrentDirectory, ".env");
        var value = ReadKeyFromEnvFile(currentDirEnvPath, key);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        // Check user home directory
        var homeEnvPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".env");
        return ReadKeyFromEnvFile(homeEnvPath, key);
    }

    /// <summary>
    /// Reads a specific key from a .env file.
    /// </summary>
    private static string? ReadKeyFromEnvFile(string filePath, string key)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            foreach (var line in File.ReadLines(filePath))
            {
                var trimmedLine = line.Trim();

                // Skip empty lines and comments
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith('#'))
                {
                    continue;
                }

                // Parse KEY=VALUE format
                var equalsIndex = trimmedLine.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                var lineKey = trimmedLine[..equalsIndex].Trim();
                if (!string.Equals(lineKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var lineValue = trimmedLine[(equalsIndex + 1)..].Trim();

                // Remove surrounding quotes if present
                if ((lineValue.StartsWith('"') && lineValue.EndsWith('"')) ||
                    (lineValue.StartsWith('\'') && lineValue.EndsWith('\'')))
                {
                    lineValue = lineValue[1..^1];
                }

                return lineValue;
            }
        }
        catch
        {
            // Silently ignore errors reading .env file
        }

        return null;
    }
}
