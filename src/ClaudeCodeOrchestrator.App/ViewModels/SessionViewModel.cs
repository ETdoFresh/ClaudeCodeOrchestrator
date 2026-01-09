using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeCodeOrchestrator.Core.Models;

namespace ClaudeCodeOrchestrator.App.ViewModels;

/// <summary>
/// View model for a Claude session.
/// </summary>
public partial class SessionViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _title = "New Session";

    [ObservableProperty]
    private SessionState _state = SessionState.Starting;

    [ObservableProperty]
    private string _worktreeId = string.Empty;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private bool _isWaitingForInput;

    [ObservableProperty]
    private decimal _totalCost;

    public ObservableCollection<MessageViewModel> Messages { get; } = new();

    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;

        var message = InputText;
        InputText = string.Empty;
        IsProcessing = true;

        // TODO: Send via session service
        Messages.Add(new UserMessageViewModel { Content = message });
    }

    private bool CanSendMessage() => !IsProcessing && !string.IsNullOrWhiteSpace(InputText);

    [RelayCommand(CanExecute = nameof(CanInterrupt))]
    private async Task InterruptAsync()
    {
        // TODO: Interrupt via session service
    }

    private bool CanInterrupt() => IsProcessing;
}

/// <summary>
/// Base class for message view models.
/// </summary>
public abstract partial class MessageViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _uuid = string.Empty;

    [ObservableProperty]
    private DateTime _timestamp = DateTime.UtcNow;
}

/// <summary>
/// View model for user messages.
/// </summary>
public partial class UserMessageViewModel : MessageViewModel
{
    [ObservableProperty]
    private string _content = string.Empty;
}

/// <summary>
/// View model for assistant messages.
/// </summary>
public partial class AssistantMessageViewModel : MessageViewModel
{
    [ObservableProperty]
    private string _textContent = string.Empty;

    [ObservableProperty]
    private string? _thinkingContent;

    [ObservableProperty]
    private bool _showThinking;

    public ObservableCollection<ToolUseViewModel> ToolUses { get; } = new();
}

/// <summary>
/// View model for tool use.
/// </summary>
public partial class ToolUseViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _toolName = string.Empty;

    [ObservableProperty]
    private string _inputJson = string.Empty;

    [ObservableProperty]
    private string? _outputJson;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private ToolUseStatus _status = ToolUseStatus.Pending;

    /// <summary>
    /// Gets a brief description of the tool action.
    /// </summary>
    public string ActionDescription
    {
        get
        {
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(InputJson);
                var root = json.RootElement;

                return ToolName switch
                {
                    "Read" => GetStringProperty(root, "file_path") is string path
                        ? $"Reading {System.IO.Path.GetFileName(path)}"
                        : "Reading file",
                    "Edit" => GetStringProperty(root, "file_path") is string editPath
                        ? $"Editing {System.IO.Path.GetFileName(editPath)}"
                        : "Editing file",
                    "Write" => GetStringProperty(root, "file_path") is string writePath
                        ? $"Writing {System.IO.Path.GetFileName(writePath)}"
                        : "Writing file",
                    "Bash" => GetStringProperty(root, "description") is string desc
                        ? desc
                        : GetStringProperty(root, "command") is string cmd
                            ? cmd.Length > 50 ? cmd[..50] + "..." : cmd
                            : "Running command",
                    "Glob" => GetStringProperty(root, "pattern") is string pattern
                        ? $"Finding {pattern}"
                        : "Finding files",
                    "Grep" => GetStringProperty(root, "pattern") is string grepPattern
                        ? $"Searching for '{grepPattern}'"
                        : "Searching",
                    "Task" => GetStringProperty(root, "description") is string taskDesc
                        ? taskDesc
                        : "Running task",
                    "TodoWrite" => "Updating todo list",
                    _ => ToolName
                };
            }
            catch
            {
                return ToolName;
            }
        }
    }

    /// <summary>
    /// Gets the file path if this is a file operation.
    /// </summary>
    public string? FilePath
    {
        get
        {
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(InputJson);
                return GetStringProperty(json.RootElement, "file_path");
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Gets the old string for Edit operations (for diff display).
    /// </summary>
    public string? OldString
    {
        get
        {
            if (ToolName != "Edit") return null;
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(InputJson);
                return GetStringProperty(json.RootElement, "old_string");
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Gets the new string for Edit operations (for diff display).
    /// </summary>
    public string? NewString
    {
        get
        {
            if (ToolName != "Edit") return null;
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(InputJson);
                return GetStringProperty(json.RootElement, "new_string");
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Gets the content for Write operations.
    /// </summary>
    public string? WriteContent
    {
        get
        {
            if (ToolName != "Write") return null;
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(InputJson);
                return GetStringProperty(json.RootElement, "content");
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Whether this is an Edit operation with diff data.
    /// </summary>
    public bool HasDiff => ToolName == "Edit" && OldString != null && NewString != null;

    /// <summary>
    /// Whether this is a Write operation with content.
    /// </summary>
    public bool HasWriteContent => ToolName == "Write" && WriteContent != null;

    /// <summary>
    /// Icon for the tool type.
    /// </summary>
    public string ToolIcon => ToolName switch
    {
        "Read" => "📄",
        "Edit" => "✏️",
        "Write" => "📝",
        "Bash" => "💻",
        "Glob" => "🔍",
        "Grep" => "🔎",
        "Task" => "🤖",
        "TodoWrite" => "📋",
        _ => "⚡"
    };

    private static string? GetStringProperty(System.Text.Json.JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String)
            return prop.GetString();
        return null;
    }
}

/// <summary>
/// Status of a tool use.
/// </summary>
public enum ToolUseStatus
{
    Pending,
    Running,
    Completed,
    Failed
}
