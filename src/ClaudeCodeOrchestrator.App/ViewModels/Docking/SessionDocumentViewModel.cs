using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeCodeOrchestrator.App.Services;
using ClaudeCodeOrchestrator.Core.Models;
using ClaudeCodeOrchestrator.Core.Services;
using ClaudeCodeOrchestrator.SDK.Messages;

namespace ClaudeCodeOrchestrator.App.ViewModels.Docking;

/// <summary>
/// Document view model for a session tab.
/// </summary>
public partial class SessionDocumentViewModel : DocumentViewModelBase, IDisposable
{
    private readonly ISessionService? _sessionService;
    private readonly IDispatcher? _dispatcher;
    private bool _disposed;

    private string _sessionId = string.Empty;
    private string _worktreeId = string.Empty;
    private SessionState _state = SessionState.Starting;
    private string _inputText = string.Empty;
    private bool _isProcessing;
    private decimal _totalCost;
    private string _worktreeBranch = string.Empty;

    public string SessionId
    {
        get => _sessionId;
        set => SetProperty(ref _sessionId, value);
    }

    public string WorktreeId
    {
        get => _worktreeId;
        set => SetProperty(ref _worktreeId, value);
    }

    public SessionState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    public string InputText
    {
        get => _inputText;
        set
        {
            if (SetProperty(ref _inputText, value))
            {
                SendMessageCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set
        {
            if (SetProperty(ref _isProcessing, value))
            {
                SendMessageCommand.NotifyCanExecuteChanged();
                InterruptCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public decimal TotalCost
    {
        get => _totalCost;
        set => SetProperty(ref _totalCost, value);
    }

    public string WorktreeBranch
    {
        get => _worktreeBranch;
        set => SetProperty(ref _worktreeBranch, value);
    }

    public ObservableCollection<MessageViewModel> Messages { get; } = new();

    /// <summary>
    /// Default constructor for design-time and welcome document.
    /// </summary>
    public SessionDocumentViewModel()
    {
        Id = Guid.NewGuid().ToString();
        Title = "New Session";
        CanClose = true;
        CanFloat = true;
    }

    /// <summary>
    /// Constructor for session-backed documents.
    /// </summary>
    public SessionDocumentViewModel(string sessionId, string title, string worktreeBranch, string worktreeId)
    {
        SessionId = sessionId;
        WorktreeId = worktreeId;
        Id = sessionId;
        Title = title.Length > 30 ? title[..27] + "..." : title;
        WorktreeBranch = worktreeBranch;
        CanClose = true;
        CanFloat = true;

        // Get services
        _sessionService = ServiceLocator.GetService<ISessionService>();
        _dispatcher = ServiceLocator.GetService<IDispatcher>();

        // Subscribe to session events
        if (_sessionService != null)
        {
            _sessionService.MessageReceived += OnMessageReceived;
            _sessionService.SessionStateChanged += OnSessionStateChanged;
        }
    }

    private void OnMessageReceived(object? sender, SessionMessageEventArgs e)
    {
        if (e.SessionId != SessionId) return;

        _dispatcher?.Post(() => ProcessMessage(e.Message));
    }

    private void ProcessMessage(ISDKMessage message)
    {
        switch (message)
        {
            case SDKAssistantMessage assistantMsg:
                ProcessAssistantMessage(assistantMsg);
                break;

            case SDKResultMessage resultMsg:
                ProcessResultMessage(resultMsg);
                break;

            // Could handle SDKStreamEvent for real-time streaming updates
        }
    }

    private void ProcessAssistantMessage(SDKAssistantMessage msg)
    {
        var vm = new AssistantMessageViewModel { Uuid = msg.Uuid };

        foreach (var block in msg.Message.Content)
        {
            switch (block)
            {
                case TextContentBlock textBlock:
                    vm.TextContent += textBlock.Text;
                    break;

                case ThinkingContentBlock thinkingBlock:
                    vm.ThinkingContent = thinkingBlock.Thinking;
                    break;

                case ToolUseContentBlock toolBlock:
                    vm.ToolUses.Add(new ToolUseViewModel
                    {
                        Id = toolBlock.Id,
                        ToolName = toolBlock.Name,
                        InputJson = toolBlock.Input?.ToString() ?? "{}",
                        Status = ToolUseStatus.Running
                    });
                    break;

                case ToolResultContentBlock toolResultBlock:
                    // Find and update the corresponding tool use
                    var toolUse = vm.ToolUses.FirstOrDefault(t => t.Id == toolResultBlock.ToolUseId);
                    if (toolUse != null)
                    {
                        toolUse.OutputJson = toolResultBlock.Content;
                        toolUse.Status = toolResultBlock.IsError ? ToolUseStatus.Failed : ToolUseStatus.Completed;
                    }
                    break;
            }
        }

        Messages.Add(vm);
    }

    private void ProcessResultMessage(SDKResultMessage msg)
    {
        TotalCost = msg.TotalCostUsd;
        IsProcessing = false;

        State = msg.IsError ? SessionState.Error : SessionState.Completed;
    }

    private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        if (e.Session.Id != SessionId) return;

        _dispatcher?.Post(() =>
        {
            State = e.Session.State;
            IsProcessing = e.Session.State == SessionState.Processing;
        });
    }

    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;
        if (_sessionService is null) return;

        var text = InputText;
        InputText = string.Empty;

        // Add user message to UI
        Messages.Add(new UserMessageViewModel
        {
            Uuid = Guid.NewGuid().ToString(),
            Content = text
        });
        IsProcessing = true;

        try
        {
            await _sessionService.SendMessageAsync(SessionId, text);
        }
        catch
        {
            IsProcessing = false;
            // Error will be shown via state change event
        }
    }

    private bool CanSendMessage() => !IsProcessing && !string.IsNullOrWhiteSpace(InputText);

    [RelayCommand(CanExecute = nameof(CanInterrupt))]
    private async Task InterruptAsync()
    {
        if (_sessionService is null) return;

        try
        {
            await _sessionService.InterruptSessionAsync(SessionId);
        }
        catch
        {
            // Error handling
        }
    }

    private bool CanInterrupt() => IsProcessing;

    /// <summary>
    /// Loads existing messages from a session (for resumed sessions with history).
    /// </summary>
    public void LoadMessagesFromSession(Session session)
    {
        if (session.Messages.Count == 0) return;

        State = session.State;
        TotalCost = session.TotalCostUsd;

        foreach (var message in session.Messages)
        {
            switch (message)
            {
                case SDKUserMessage userMsg:
                    Messages.Add(new UserMessageViewModel
                    {
                        Uuid = userMsg.Uuid,
                        Content = userMsg.Message?.Content?.GetText() ?? string.Empty
                    });
                    break;

                case SDKAssistantMessage assistantMsg:
                    var vm = new AssistantMessageViewModel { Uuid = assistantMsg.Uuid };
                    foreach (var block in assistantMsg.Message.Content)
                    {
                        if (block is TextContentBlock textBlock)
                        {
                            vm.TextContent += textBlock.Text;
                        }
                    }
                    Messages.Add(vm);
                    break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_sessionService != null)
        {
            _sessionService.MessageReceived -= OnMessageReceived;
            _sessionService.SessionStateChanged -= OnSessionStateChanged;
        }
    }
}
