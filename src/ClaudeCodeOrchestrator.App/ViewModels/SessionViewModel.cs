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
