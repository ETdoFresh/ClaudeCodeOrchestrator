using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.Input;
using ClaudeCodeOrchestrator.App.Models;
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
    private bool _isPreview;
    private bool _isReadyToMerge;
    private List<ImageAttachment> _pendingAttachments = new();

    /// <summary>
    /// Callback to refresh worktrees when session completes.
    /// </summary>
    public Func<Task>? OnSessionCompleted { get; set; }

    /// <summary>
    /// Callback when processing state changes, passing (worktreeId, isProcessing).
    /// </summary>
    public Action<string, bool>? OnProcessingStateChanged { get; set; }

    /// <summary>
    /// Callback to merge this worktree.
    /// </summary>
    public Func<string, Task>? OnMergeRequested { get; set; }

    /// <summary>
    /// Callback to run the configured executable in this session's worktree.
    /// </summary>
    public Func<string, Task>? OnRunRequested { get; set; }

    /// <summary>
    /// Callback to open VS Code in this session's worktree.
    /// </summary>
    public Func<string, Task>? OnOpenInVSCodeRequested { get; set; }

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
                QueueMessageCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(ShowStopButton));
                OnPropertyChanged(nameof(ShowQueueButton));
                OnPropertyChanged(nameof(ActionButtonText));
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
                OnPropertyChanged(nameof(ShowSendButton));
                OnPropertyChanged(nameof(ShowStopButton));
                OnPropertyChanged(nameof(ShowQueueButton));
                OnPropertyChanged(nameof(ActionButtonText));
                OnPropertyChanged(nameof(ShowEmptyState));

                // Notify worktree of processing state change
                if (!string.IsNullOrEmpty(WorktreeId))
                {
                    OnProcessingStateChanged?.Invoke(WorktreeId, value);
                }
            }
        }
    }

    /// <summary>
    /// True when not processing (show Send button).
    /// </summary>
    public bool ShowSendButton => !IsProcessing;

    /// <summary>
    /// True when processing and input is empty (show Stop button).
    /// </summary>
    public bool ShowStopButton => IsProcessing && string.IsNullOrWhiteSpace(InputText);

    /// <summary>
    /// True when processing and has input text (show Queue button).
    /// </summary>
    public bool ShowQueueButton => IsProcessing && !string.IsNullOrWhiteSpace(InputText);

    /// <summary>
    /// Gets the action button text based on current state.
    /// </summary>
    public string ActionButtonText => IsProcessing
        ? (string.IsNullOrWhiteSpace(InputText) ? "Stop" : "Queue")
        : "Send";

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

    public bool IsPreview
    {
        get => _isPreview;
        set => SetProperty(ref _isPreview, value);
    }

    /// <summary>
    /// Indicates if this session's worktree is ready to merge.
    /// </summary>
    public bool IsReadyToMerge
    {
        get => _isReadyToMerge;
        set
        {
            if (SetProperty(ref _isReadyToMerge, value))
            {
                MergeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool _canRun;

    /// <summary>
    /// Whether the run button should be visible (executable is configured).
    /// </summary>
    public bool CanRun
    {
        get => _canRun;
        set => SetProperty(ref _canRun, value);
    }

    private bool _canOpenInVSCode;

    /// <summary>
    /// Whether the VS Code button should be visible (VS Code is available).
    /// </summary>
    public bool CanOpenInVSCode
    {
        get => _canOpenInVSCode;
        set => SetProperty(ref _canOpenInVSCode, value);
    }

    public ObservableCollection<MessageViewModel> Messages { get; } = new();

    /// <summary>
    /// True when the session has no messages and is not processing.
    /// Used to show a welcome/empty state message.
    /// </summary>
    public bool ShowEmptyState => Messages.Count == 0 && !IsProcessing;

    /// <summary>
    /// Sets the pending image attachments for the next message.
    /// </summary>
    public void SetAttachments(List<ImageAttachment> attachments)
    {
        _pendingAttachments = attachments;
    }

    /// <summary>
    /// Callback for the view to clear attachments after sending.
    /// </summary>
    public Action? ClearAttachmentsCallback { get; set; }

    /// <summary>
    /// Default constructor for design-time and welcome document.
    /// </summary>
    public SessionDocumentViewModel()
    {
        Id = Guid.NewGuid().ToString();
        Title = "New Session";
        CanClose = true;
        CanFloat = true;

        // Subscribe to collection changes for ShowEmptyState
        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowEmptyState));
    }

    /// <summary>
    /// Constructor for session-backed documents.
    /// </summary>
    public SessionDocumentViewModel(string sessionId, string title, string worktreeBranch, string worktreeId, bool isActiveSession = true)
    {
        SessionId = sessionId;
        WorktreeId = worktreeId;
        Id = sessionId;
        Title = title.Length > 30 ? title[..27] + "..." : title;
        WorktreeBranch = worktreeBranch;
        CanClose = true;
        CanFloat = true;

        // Subscribe to collection changes for ShowEmptyState
        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowEmptyState));

        // If this is a new active session, it starts in processing state
        IsProcessing = isActiveSession;

        // Get services
        _sessionService = ServiceLocator.GetService<ISessionService>();
        _dispatcher = ServiceLocator.GetService<IDispatcher>();

        // Subscribe to session events
        if (_sessionService != null)
        {
            _sessionService.MessageReceived += OnMessageReceived;
            _sessionService.SessionStateChanged += OnSessionStateChanged;
            _sessionService.SessionTitleUpdated += OnSessionTitleUpdated;
        }
    }

    private void OnSessionTitleUpdated(object? sender, SessionTitleUpdatedEventArgs e)
    {
        if (e.SessionId != SessionId) return;

        _dispatcher?.Post(() =>
        {
            // Update the document title (truncate if needed)
            Title = e.NewTitle.Length > 30 ? e.NewTitle[..27] + "..." : e.NewTitle;
        });
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

    private async void ProcessResultMessage(SDKResultMessage msg)
    {
        TotalCost = msg.TotalCostUsd;
        IsProcessing = false;

        State = msg.IsError ? SessionState.Error : SessionState.Completed;

        // Session completed, refresh worktrees to show updated status
        if (OnSessionCompleted != null)
        {
            await OnSessionCompleted();
        }
    }

    private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        if (e.Session.Id != SessionId) return;

        _dispatcher?.Post(async () =>
        {
            State = e.Session.State;
            // Session is processing when Active (Claude working) or Processing (sending message)
            IsProcessing = e.Session.State is SessionState.Active or SessionState.Processing or SessionState.Starting;

            // If session was cancelled or completed, refresh worktrees
            if (e.Session.State is SessionState.Cancelled or SessionState.Completed or SessionState.Error)
            {
                if (OnSessionCompleted != null)
                {
                    await OnSessionCompleted();
                }
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        if (_sessionService is null) return;

        var text = InputText;
        var attachments = _pendingAttachments.ToList();
        InputText = string.Empty;
        _pendingAttachments.Clear();
        ClearAttachmentsCallback?.Invoke();

        // Add user message to UI (only if there's actual content or images)
        if (!string.IsNullOrWhiteSpace(text) || attachments.Count > 0)
        {
            var userVm = new UserMessageViewModel
            {
                Uuid = Guid.NewGuid().ToString(),
                Content = text
            };

            // Add image attachments to the message for display
            foreach (var attachment in attachments)
            {
                userVm.Images.Add(attachment);
            }

            Messages.Add(userVm);
        }
        IsProcessing = true;

        try
        {
            await _sessionService.SendMessageAsync(SessionId, text, attachments);
        }
        catch
        {
            IsProcessing = false;
            // Error will be shown via state change event
        }
    }

    private bool CanSendMessage() => !IsProcessing;

    [RelayCommand(CanExecute = nameof(CanQueueMessage))]
    private async Task QueueMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;
        if (_sessionService is null) return;

        var text = InputText;
        var attachments = _pendingAttachments.ToList();
        InputText = string.Empty;
        _pendingAttachments.Clear();
        ClearAttachmentsCallback?.Invoke();

        // Add user message to UI immediately (message will be injected at next tool boundary)
        var userVm = new UserMessageViewModel
        {
            Uuid = Guid.NewGuid().ToString(),
            Content = text
        };

        // Add image attachments to the message for display
        foreach (var attachment in attachments)
        {
            userVm.Images.Add(attachment);
        }

        Messages.Add(userVm);

        try
        {
            // Send the message immediately - SDK will inject it at next tool boundary
            await _sessionService.SendMessageAsync(SessionId, text, attachments);
        }
        catch
        {
            // Error will be handled via state change events
        }
    }

    private bool CanQueueMessage() => IsProcessing && !string.IsNullOrWhiteSpace(InputText);

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

    [RelayCommand(CanExecute = nameof(CanMerge))]
    private async Task MergeAsync()
    {
        if (OnMergeRequested != null && !string.IsNullOrEmpty(WorktreeId))
        {
            await OnMergeRequested(WorktreeId);
        }
    }

    private bool CanMerge() => IsReadyToMerge && !IsProcessing;

    [RelayCommand]
    private async Task RunAsync()
    {
        if (OnRunRequested != null && !string.IsNullOrEmpty(WorktreeId))
        {
            await OnRunRequested(WorktreeId);
        }
    }

    [RelayCommand]
    private async Task OpenInVSCodeAsync()
    {
        if (OnOpenInVSCodeRequested != null && !string.IsNullOrEmpty(WorktreeId))
        {
            await OnOpenInVSCodeRequested(WorktreeId);
        }
    }

    [RelayCommand]
    private async Task CopyChatAsync()
    {
        var clipboard = GetClipboard();
        if (clipboard == null) return;

        var sb = new StringBuilder();
        var lastWasAssistant = false;

        foreach (var message in Messages)
        {
            switch (message)
            {
                case UserMessageViewModel user:
                    var userContent = user.Content?.Trim();
                    if (!string.IsNullOrEmpty(userContent))
                    {
                        sb.AppendLine("## User");
                        sb.AppendLine(userContent);
                        sb.AppendLine();
                        lastWasAssistant = false;
                    }
                    break;

                case AssistantMessageViewModel assistant:
                    var assistantContent = assistant.TextContent?.Trim();
                    if (!string.IsNullOrEmpty(assistantContent))
                    {
                        // Combine consecutive assistant messages
                        if (lastWasAssistant)
                        {
                            sb.AppendLine(assistantContent);
                            sb.AppendLine();
                        }
                        else
                        {
                            sb.AppendLine("## Assistant");
                            sb.AppendLine(assistantContent);
                            sb.AppendLine();
                        }
                        lastWasAssistant = true;
                    }
                    break;
            }
        }

        await clipboard.SetTextAsync(sb.ToString().TrimEnd());
    }

    private static IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
        return null;
    }

    /// <summary>
    /// Adds a user message to the UI from an external source (e.g., conflict resolution).
    /// </summary>
    public void AddExternalUserMessage(string content)
    {
        Messages.Add(new UserMessageViewModel
        {
            Uuid = Guid.NewGuid().ToString(),
            Content = content
        });
        IsProcessing = true;
    }

    /// <summary>
    /// Loads existing messages from a session (for resumed sessions with history).
    /// </summary>
    public void LoadMessagesFromSession(Session session)
    {
        if (session.Messages.Count == 0) return;

        State = session.State;
        TotalCost = session.TotalCostUsd;
        // Update IsProcessing based on session state
        IsProcessing = session.State is SessionState.Active or SessionState.Processing or SessionState.Starting;

        foreach (var message in session.Messages)
        {
            switch (message)
            {
                case SDKUserMessage userMsg:
                    var userVm = new UserMessageViewModel
                    {
                        Uuid = userMsg.Uuid,
                        Content = userMsg.Message?.Content?.GetText() ?? string.Empty
                    };

                    // Extract images from content blocks
                    if (userMsg.Message?.Content?.Blocks != null)
                    {
                        foreach (var block in userMsg.Message.Content.Blocks)
                        {
                            if (block.Type == "image" && block.Content != null)
                            {
                                try
                                {
                                    // Content is an anonymous object, serialize and deserialize to extract properties
                                    var contentJson = JsonSerializer.Serialize(block.Content);
                                    using var doc = JsonDocument.Parse(contentJson);
                                    var root = doc.RootElement;

                                    if (root.TryGetProperty("data", out var dataElement) &&
                                        root.TryGetProperty("media_type", out var mediaTypeElement))
                                    {
                                        var base64Data = dataElement.GetString();
                                        var mediaType = mediaTypeElement.GetString();

                                        if (!string.IsNullOrEmpty(base64Data) && !string.IsNullOrEmpty(mediaType))
                                        {
                                            var imageAttachment = ImageAttachment.FromBase64(base64Data, mediaType);
                                            userVm.Images.Add(imageAttachment);
                                        }
                                    }
                                }
                                catch
                                {
                                    // Skip invalid image blocks
                                }
                            }
                        }
                    }

                    Messages.Add(userVm);
                    break;

                case SDKAssistantMessage assistantMsg:
                    var vm = new AssistantMessageViewModel { Uuid = assistantMsg.Uuid };
                    foreach (var block in assistantMsg.Message.Content)
                    {
                        if (block is TextContentBlock textBlock)
                        {
                            vm.TextContent += textBlock.Text;
                        }
                        else if (block is ToolUseContentBlock toolBlock)
                        {
                            vm.ToolUses.Add(new ToolUseViewModel
                            {
                                Id = toolBlock.Id,
                                ToolName = toolBlock.Name,
                                InputJson = toolBlock.Input?.ToString() ?? "{}",
                                Status = ToolUseStatus.Completed // From history, assume completed
                            });
                        }
                    }
                    // Add if there's text content OR tool uses
                    if (!string.IsNullOrEmpty(vm.TextContent) || vm.ToolUses.Count > 0)
                    {
                        Messages.Add(vm);
                    }
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
            _sessionService.SessionTitleUpdated -= OnSessionTitleUpdated;
        }
    }
}
