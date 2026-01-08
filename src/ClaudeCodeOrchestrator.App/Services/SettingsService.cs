using System.Text.Json;

namespace ClaudeCodeOrchestrator.App.Services;

/// <summary>
/// Service for persisting application settings to a JSON file.
/// </summary>
public class SettingsService : ISettingsService
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeCodeOrchestrator");

    private static readonly string SettingsPath = Path.Combine(
        SettingsDirectory, "settings.json");

    private AppSettings _settings = new();

    public string? LastRepositoryPath => _settings.LastRepositoryPath;

    public void SetLastRepositoryPath(string? path)
    {
        _settings.LastRepositoryPath = path;
        Save();
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            if (settings != null)
            {
                _settings = settings;
            }
        }
        catch
        {
            // Ignore errors loading settings, use defaults
            _settings = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);

            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Ignore errors saving settings
        }
    }

    private class AppSettings
    {
        public string? LastRepositoryPath { get; set; }
    }
}
