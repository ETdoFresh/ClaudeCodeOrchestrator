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

    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    public string? LastRepositoryPath => _settings.LastRepositoryPath;

    public void SetLastRepositoryPath(string? path)
    {
        var oldValue = _settings.LastRepositoryPath;
        _settings.LastRepositoryPath = path;
        Save();
        RaiseSettingChanged(nameof(LastRepositoryPath), oldValue, path);
    }

    private const int MaxRecentRepositories = 4;

    public IReadOnlyList<string> RecentRepositories => _settings.RecentRepositories.AsReadOnly();

    public void AddRecentRepository(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        // Normalize path for comparison
        var normalizedPath = Path.GetFullPath(path);

        // Remove if already exists (to move it to the front)
        _settings.RecentRepositories.RemoveAll(p =>
            string.Equals(Path.GetFullPath(p), normalizedPath, StringComparison.OrdinalIgnoreCase));

        // Add to the front
        _settings.RecentRepositories.Insert(0, path);

        // Keep only the most recent entries
        if (_settings.RecentRepositories.Count > MaxRecentRepositories)
        {
            _settings.RecentRepositories.RemoveRange(MaxRecentRepositories,
                _settings.RecentRepositories.Count - MaxRecentRepositories);
        }

        Save();
        RaiseSettingChanged(nameof(RecentRepositories), null, _settings.RecentRepositories);
    }

    public bool ShowMergeConfirmation
    {
        get => _settings.ShowMergeConfirmation;
        set
        {
            if (_settings.ShowMergeConfirmation == value) return;
            var oldValue = _settings.ShowMergeConfirmation;
            _settings.ShowMergeConfirmation = value;
            Save();
            RaiseSettingChanged(nameof(ShowMergeConfirmation), oldValue, value);
        }
    }

    public bool ShowDeleteConfirmation
    {
        get => _settings.ShowDeleteConfirmation;
        set
        {
            if (_settings.ShowDeleteConfirmation == value) return;
            var oldValue = _settings.ShowDeleteConfirmation;
            _settings.ShowDeleteConfirmation = value;
            Save();
            RaiseSettingChanged(nameof(ShowDeleteConfirmation), oldValue, value);
        }
    }

    public bool ShowCloseSessionConfirmation
    {
        get => _settings.ShowCloseSessionConfirmation;
        set
        {
            if (_settings.ShowCloseSessionConfirmation == value) return;
            var oldValue = _settings.ShowCloseSessionConfirmation;
            _settings.ShowCloseSessionConfirmation = value;
            Save();
            RaiseSettingChanged(nameof(ShowCloseSessionConfirmation), oldValue, value);
        }
    }

    public bool AutoOpenSessionOnWorktreeCreate
    {
        get => _settings.AutoOpenSessionOnWorktreeCreate;
        set
        {
            if (_settings.AutoOpenSessionOnWorktreeCreate == value) return;
            var oldValue = _settings.AutoOpenSessionOnWorktreeCreate;
            _settings.AutoOpenSessionOnWorktreeCreate = value;
            Save();
            RaiseSettingChanged(nameof(AutoOpenSessionOnWorktreeCreate), oldValue, value);
        }
    }

    public bool ShowOutputPanelByDefault
    {
        get => _settings.ShowOutputPanelByDefault;
        set
        {
            if (_settings.ShowOutputPanelByDefault == value) return;
            var oldValue = _settings.ShowOutputPanelByDefault;
            _settings.ShowOutputPanelByDefault = value;
            Save();
            RaiseSettingChanged(nameof(ShowOutputPanelByDefault), oldValue, value);
        }
    }

    public bool CompactWorktreeList
    {
        get => _settings.CompactWorktreeList;
        set
        {
            if (_settings.CompactWorktreeList == value) return;
            var oldValue = _settings.CompactWorktreeList;
            _settings.CompactWorktreeList = value;
            Save();
            RaiseSettingChanged(nameof(CompactWorktreeList), oldValue, value);
        }
    }

    public bool ShowWorktreeStatusBadges
    {
        get => _settings.ShowWorktreeStatusBadges;
        set
        {
            if (_settings.ShowWorktreeStatusBadges == value) return;
            var oldValue = _settings.ShowWorktreeStatusBadges;
            _settings.ShowWorktreeStatusBadges = value;
            Save();
            RaiseSettingChanged(nameof(ShowWorktreeStatusBadges), oldValue, value);
        }
    }

    public bool AutoSaveSessionHistory
    {
        get => _settings.AutoSaveSessionHistory;
        set
        {
            if (_settings.AutoSaveSessionHistory == value) return;
            var oldValue = _settings.AutoSaveSessionHistory;
            _settings.AutoSaveSessionHistory = value;
            Save();
            RaiseSettingChanged(nameof(AutoSaveSessionHistory), oldValue, value);
        }
    }

    public int CachedPushBadgeCount
    {
        get => _settings.CachedPushBadgeCount;
        set
        {
            if (_settings.CachedPushBadgeCount == value) return;
            _settings.CachedPushBadgeCount = value;
            Save();
            // No need to raise event for cached values
        }
    }

    public string? OpenRouterApiKey
    {
        get => _settings.OpenRouterApiKey;
        set
        {
            if (_settings.OpenRouterApiKey == value) return;
            var oldValue = _settings.OpenRouterApiKey;
            _settings.OpenRouterApiKey = value;
            Save();
            RaiseSettingChanged(nameof(OpenRouterApiKey), oldValue, value);
        }
    }

    private void RaiseSettingChanged(string propertyName, object? oldValue, object? newValue)
    {
        SettingChanged?.Invoke(this, new SettingChangedEventArgs(propertyName, oldValue, newValue));
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
        public List<string> RecentRepositories { get; set; } = new();
        public bool ShowMergeConfirmation { get; set; } = true;
        public bool ShowDeleteConfirmation { get; set; } = true;
        public bool ShowCloseSessionConfirmation { get; set; } = true;
        public bool AutoOpenSessionOnWorktreeCreate { get; set; } = true;
        public bool ShowOutputPanelByDefault { get; set; } = false;
        public bool CompactWorktreeList { get; set; } = false;
        public bool ShowWorktreeStatusBadges { get; set; } = true;
        public bool AutoSaveSessionHistory { get; set; } = true;
        public int CachedPushBadgeCount { get; set; } = 0;
        public string? OpenRouterApiKey { get; set; }
    }
}
