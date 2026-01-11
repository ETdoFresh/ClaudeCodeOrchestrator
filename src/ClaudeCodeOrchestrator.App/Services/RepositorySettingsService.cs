using System.Diagnostics;
using System.Text.Json;
using ClaudeCodeOrchestrator.App.Models;
using ClaudeCodeOrchestrator.Git.Services;

namespace ClaudeCodeOrchestrator.App.Services;

/// <summary>
/// Service for managing per-repository settings stored in .claude-repo-config.json.
/// </summary>
public class RepositorySettingsService : IRepositorySettingsService
{
    private const string SettingsFileName = ".claude-repo-config.json";

    private readonly IGitService _gitService;
    private RepositorySettings? _settings;
    private string? _repositoryPath;
    private string? _settingsFilePath;
    private bool? _isVSCodeAvailable;

    public RepositorySettingsService(IGitService gitService)
    {
        _gitService = gitService;
        // Detect VS Code availability on startup
        _ = DetectVSCodeAsync();
    }

    public RepositorySettings? Settings => _settings;

    public bool HasExecutable => !string.IsNullOrWhiteSpace(_settings?.Executable);

    public bool IsVSCodeAvailable => _isVSCodeAvailable ?? false;

    public string? RepositoryPath => _repositoryPath;

    public event EventHandler? SettingsChanged;

    public void Load(string repositoryPath)
    {
        _repositoryPath = repositoryPath;
        _settingsFilePath = Path.Combine(repositoryPath, SettingsFileName);

        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                _settings = JsonSerializer.Deserialize<RepositorySettings>(json) ?? new RepositorySettings();
            }
            else
            {
                _settings = new RepositorySettings();
            }
        }
        catch
        {
            // If there's an error reading settings, start with defaults
            _settings = new RepositorySettings();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Save()
    {
        if (_settingsFilePath is null || _settings is null || _repositoryPath is null)
            return;

        try
        {
            var isNewFile = !File.Exists(_settingsFilePath);

            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_settingsFilePath, json);

            // Auto-commit the settings file
            _ = CommitSettingsFileAsync(isNewFile);
        }
        catch
        {
            // Ignore errors saving settings
        }
    }

    private async Task CommitSettingsFileAsync(bool isNewFile)
    {
        if (_repositoryPath is null)
            return;

        try
        {
            // Stage and commit the settings file using git command
            var addPsi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"add \"{SettingsFileName}\"",
                WorkingDirectory = _repositoryPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var addProcess = Process.Start(addPsi);
            if (addProcess != null)
            {
                await addProcess.WaitForExitAsync();
                if (addProcess.ExitCode != 0)
                    return;
            }

            // Check if there are staged changes to commit
            var diffPsi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "diff --cached --quiet",
                WorkingDirectory = _repositoryPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var diffProcess = Process.Start(diffPsi);
            if (diffProcess != null)
            {
                await diffProcess.WaitForExitAsync();
                // Exit code 0 means no changes, 1 means there are changes
                if (diffProcess.ExitCode == 0)
                    return; // Nothing to commit
            }

            var commitMessage = isNewFile
                ? $"Add {SettingsFileName}"
                : $"Update {SettingsFileName}";

            var commitPsi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"commit -m \"{commitMessage}\"",
                WorkingDirectory = _repositoryPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var commitProcess = Process.Start(commitPsi);
            if (commitProcess != null)
            {
                await commitProcess.WaitForExitAsync();
            }
        }
        catch
        {
            // Ignore errors committing settings
        }
    }

    public void SetExecutable(string? executable)
    {
        if (_settings is null)
            _settings = new RepositorySettings();

        _settings.Executable = executable;
        Save();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<bool> RunExecutableAsync(string workingDirectory)
    {
        if (!HasExecutable || _settings?.Executable is null)
            return Task.FromResult(false);

        var executable = _settings.Executable.Trim();

        try
        {
            // Check if it's a file path that should be opened with the OS handler
            var fullPath = Path.IsPathRooted(executable)
                ? executable
                : Path.Combine(workingDirectory, executable);

            // If it's a single file path that exists, open it with the OS handler
            if (!executable.Contains(' ') && File.Exists(fullPath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fullPath,
                    UseShellExecute = true,
                    WorkingDirectory = workingDirectory
                };
                Process.Start(psi);
                return Task.FromResult(true);
            }

            // Otherwise, treat it as a command with optional arguments
            string fileName;
            string arguments;

            // Check if the executable contains arguments
            var firstSpaceIndex = executable.IndexOf(' ');
            if (firstSpaceIndex > 0)
            {
                fileName = executable[..firstSpaceIndex];
                arguments = executable[(firstSpaceIndex + 1)..];
            }
            else
            {
                fileName = executable;
                arguments = string.Empty;
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = true, // Use shell to support commands like "dotnet"
                CreateNoWindow = false
            };

            Process.Start(processStartInfo);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public void Clear()
    {
        _settings = null;
        _repositoryPath = null;
        _settingsFilePath = null;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<bool> OpenInVSCodeAsync(string workingDirectory)
    {
        if (!IsVSCodeAvailable)
            return Task.FromResult(false);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "code",
                Arguments = $"\"{workingDirectory}\"",
                UseShellExecute = true,
                CreateNoWindow = true
            };

            Process.Start(psi);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private async Task DetectVSCodeAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "code",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
                _isVSCodeAvailable = process.ExitCode == 0;
            }
            else
            {
                _isVSCodeAvailable = false;
            }
        }
        catch
        {
            _isVSCodeAvailable = false;
        }
    }
}
