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
    private decimal _accumulatedCost;
    private string _worktreeBranch = string.Empty;
    private bool _isPreview;
    private bool _isReadyToMerge;
    private List<ImageAttachment> _pendingAttachments = new();

    // Pagination support for large message histories
    private const int InitialMessageCount = 100;
    private const int LoadMoreCount = 50;
    private List<ISDKMessage> _allMessages = new();
    private int _loadedMessageCount;
    private bool _isLoadingMore;
    private bool _hasMoreMessages;
    private bool _isLoadingHistory;

    // Message virtualization - unload old messages when conversation grows
    private const int MaxMessagesInMemory = 150;
    private const int UnloadThreshold = 200;
    private const int MessagesToUnload = 100;
    private string? _claudeSessionId;
    private string? _worktreePath;
    private int _diskMessageCount;
    private int _oldestLoadedIndex;
    private bool _hasUnloadedMessages;

    /// <summary>
    /// Callback to refresh worktrees when session completes.
    /// </summary>
    public Func<Task>? OnSessionCompleted { get; set; }

    /// <summary>
    /// Callback for job iteration handling when session completes.
    /// Passes (worktreeId, finalState).
    /// </summary>
    public Func<string, Core.Models.SessionState, Task>? OnJobIterationCompleted { get; set; }

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

    /// <summary>
    /// Callback to resync session history from disk.
    /// </summary>
    public Func<string, Task>? OnResyncHistoryRequested { get; set; }

    /// <summary>
    /// Callback to delete this worktree.
    /// </summary>
    public Func<string, Task>? OnDeleteRequested { get; set; }

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
                MergeCommand.NotifyCanExecuteChanged();
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

    /// <summary>
    /// Gets or sets the accumulated cost across all session resumes.
    /// Setting this initializes the cost from persisted data.
    /// </summary>
    public decimal AccumulatedCostUsd
    {
        get => _accumulatedCost;
        set
        {
            _accumulatedCost = value;
            TotalCost = value;
        }
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

    private int? _currentIteration;

    /// <summary>
    /// Current iteration number for an active job (null if not part of a job).
    /// </summary>
    public int? CurrentIteration
    {
        get => _currentIteration;
        set
        {
            if (SetProperty(ref _currentIteration, value))
            {
                OnPropertyChanged(nameof(IterationText));
            }
        }
    }

    private int? _maxIterations;

    /// <summary>
    /// Maximum iterations for an active job (null if not part of a job).
    /// </summary>
    public int? MaxIterations
    {
        get => _maxIterations;
        set
        {
            if (SetProperty(ref _maxIterations, value))
            {
                OnPropertyChanged(nameof(IterationText));
            }
        }
    }

    /// <summary>
    /// Gets the formatted iteration text (e.g., "Iteration 1/20").
    /// Returns null if not part of a job.
    /// </summary>
    public string? IterationText => CurrentIteration.HasValue && MaxIterations.HasValue
        ? $"Iteration {CurrentIteration}/{MaxIterations}"
        : null;

    public ObservableCollection<MessageViewModel> Messages { get; } = new();

    /// <summary>
    /// True when the session has no messages, not processing, and not loading history.
    /// Used to show a welcome/empty state message.
    /// </summary>
    public bool ShowEmptyState => Messages.Count == 0 && !IsProcessing && !IsLoadingHistory;

    /// <summary>
    /// True when there are more messages that can be loaded by scrolling up.
    /// </summary>
    public bool HasMoreMessages
    {
        get => _hasMoreMessages || _hasUnloadedMessages;
        private set => SetProperty(ref _hasMoreMessages, value);
    }

    /// <summary>
    /// True when old messages have been unloaded from memory.
    /// </summary>
    public bool HasUnloadedMessages
    {
        get => _hasUnloadedMessages;
        private set => SetProperty(ref _hasUnloadedMessages, value);
    }

    /// <summary>
    /// Gets the count of messages that have been unloaded.
    /// </summary>
    public int UnloadedMessageCount => _oldestLoadedIndex;

    /// <summary>
    /// True while loading more messages (prevents concurrent loads).
    /// </summary>
    public bool IsLoadingMore
    {
        get => _isLoadingMore;
        private set => SetProperty(ref _isLoadingMore, value);
    }

    /// <summary>
    /// True while loading session history from disk.
    /// Used to show a loading spinner in the tab.
    /// </summary>
    public bool IsLoadingHistory
    {
        get => _isLoadingHistory;
        set
        {
            if (SetProperty(ref _isLoadingHistory, value))
            {
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(ShowLoadingState));
            }
        }
    }

    /// <summary>
    /// True when loading history (show loading spinner).
    /// </summary>
    public bool ShowLoadingState => IsLoadingHistory;

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
        CheckAndUnloadOldMessages();
    }

    private async void ProcessResultMessage(SDKResultMessage msg)
    {
        // Accumulate cost instead of replacing
        _accumulatedCost += msg.TotalCostUsd;
        TotalCost = _accumulatedCost;
        IsProcessing = false;

        State = msg.IsError ? SessionState.Error : SessionState.Completed;

        // Session completed, refresh worktrees to show updated status
        if (OnSessionCompleted != null)
        {
            await OnSessionCompleted();
        }

        // Handle job iteration if this is part of a job
        if (OnJobIterationCompleted != null)
        {
            await OnJobIterationCompleted(WorktreeId, State);
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

                // Handle job iteration if this is part of a job
                if (OnJobIterationCompleted != null)
                {
                    await OnJobIterationCompleted(WorktreeId, e.Session.State);
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
            CheckAndUnloadOldMessages();
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
        CheckAndUnloadOldMessages();

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
    private async Task ResyncHistoryAsync()
    {
        if (OnResyncHistoryRequested != null && !string.IsNullOrEmpty(WorktreeId))
        {
            await OnResyncHistoryRequested(WorktreeId);
        }
    }

    [RelayCommand]
    private async Task LoadMoreMessagesAction()
    {
        await LoadMoreMessagesAsync();
    }

    [RelayCommand]
    private async Task DeleteWorktreeAsync()
    {
        if (OnDeleteRequested != null && !string.IsNullOrEmpty(WorktreeId))
        {
            await OnDeleteRequested(WorktreeId);
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
    /// Only loads the last InitialMessageCount messages initially for performance.
    /// More messages can be loaded by calling LoadMoreMessages().
    /// </summary>
    public void LoadMessagesFromSession(Session session)
    {
        // Always update state and processing flag, even for empty sessions
        State = session.State;
        TotalCost = session.TotalCostUsd;
        // Update IsProcessing based on session state
        IsProcessing = session.State is SessionState.Active or SessionState.Processing or SessionState.Starting;

        if (session.Messages.Count == 0) return;

        // Store all messages for lazy loading
        _allMessages = session.Messages.ToList();

        // Determine how many messages to load initially
        var totalCount = _allMessages.Count;
        var startIndex = Math.Max(0, totalCount - InitialMessageCount);
        _loadedMessageCount = totalCount - startIndex;
        HasMoreMessages = startIndex > 0;

        // Load only the last InitialMessageCount messages
        for (var i = startIndex; i < totalCount; i++)
        {
            var viewModel = ConvertToViewModel(_allMessages[i]);
            if (viewModel != null)
            {
                Messages.Add(viewModel);
            }
        }
    }

    /// <summary>
    /// Inserts a session started indicator at the beginning of the messages.
    /// </summary>
    /// <param name="isJob">Whether this is a job session.</param>
    /// <param name="iteration">The current iteration number (for jobs).</param>
    /// <param name="maxIterations">The max iterations (for jobs).</param>
    public void InsertSessionStartedIndicator(bool isJob = false, int? iteration = null, int? maxIterations = null)
    {
        string content;
        string icon;
        SystemMessageType messageType;

        if (isJob && iteration.HasValue && maxIterations.HasValue)
        {
            content = $"New Session Started - Iteration {iteration}/{maxIterations}";
            icon = "🔄";
            messageType = SystemMessageType.IterationStarted;
        }
        else if (isJob)
        {
            content = "Job Session Started";
            icon = "🚀";
            messageType = SystemMessageType.SessionStarted;
        }
        else
        {
            content = "New Session Started";
            icon = "🚀";
            messageType = SystemMessageType.SessionStarted;
        }

        var systemMessage = new SystemMessageViewModel
        {
            Content = content,
            Icon = icon,
            MessageType = messageType,
            Timestamp = DateTime.UtcNow
        };

        // Insert at the beginning (index 0)
        Messages.Insert(0, systemMessage);
    }

    /// <summary>
    /// Appends a session/iteration indicator at the end of the messages.
    /// Used when starting a new iteration in an existing document.
    /// </summary>
    /// <param name="iteration">The current iteration number.</param>
    /// <param name="maxIterations">The max iterations.</param>
    public void AppendIterationIndicator(int iteration, int maxIterations)
    {
        var systemMessage = new SystemMessageViewModel
        {
            Content = $"New Session Started - Iteration {iteration}/{maxIterations}",
            Icon = "🔄",
            MessageType = SystemMessageType.IterationStarted,
            Timestamp = DateTime.UtcNow
        };

        // Append at the end
        Messages.Add(systemMessage);
    }

    /// <summary>
    /// Sets the context needed for loading messages from disk when they've been unloaded.
    /// </summary>
    public void SetDiskLoadingContext(string worktreePath, string claudeSessionId, int totalMessageCount)
    {
        _worktreePath = worktreePath;
        _claudeSessionId = claudeSessionId;
        _diskMessageCount = totalMessageCount;
    }

    /// <summary>
    /// Checks if old messages should be unloaded and performs the unload if needed.
    /// Called after adding new messages.
    /// </summary>
    private void CheckAndUnloadOldMessages()
    {
        if (Messages.Count <= UnloadThreshold) return;

        var toRemove = Math.Min(MessagesToUnload, Messages.Count - MaxMessagesInMemory);
        if (toRemove <= 0) return;

        // Remove oldest messages from UI
        for (var i = 0; i < toRemove; i++)
        {
            Messages.RemoveAt(0);
        }

        // Also remove from _allMessages to free memory
        if (_allMessages.Count > 0)
        {
            var removeCount = Math.Min(toRemove, _allMessages.Count);
            _allMessages.RemoveRange(0, removeCount);
        }

        // Update tracking
        _oldestLoadedIndex += toRemove;
        HasUnloadedMessages = true;
        OnPropertyChanged(nameof(UnloadedMessageCount));
        OnPropertyChanged(nameof(HasMoreMessages));
    }

    /// <summary>
    /// Loads more messages when scrolling up. Call this when user scrolls near the top.
    /// </summary>
    /// <returns>The number of messages loaded.</returns>
    public int LoadMoreMessages()
    {
        if (!HasMoreMessages || IsLoadingMore) return 0;

        IsLoadingMore = true;
        try
        {
            var totalCount = _allMessages.Count;
            var currentStartIndex = totalCount - _loadedMessageCount;

            // Calculate the new range to load
            var newStartIndex = Math.Max(0, currentStartIndex - LoadMoreCount);
            var countToLoad = currentStartIndex - newStartIndex;

            if (countToLoad <= 0)
            {
                _hasMoreMessages = false;
                OnPropertyChanged(nameof(HasMoreMessages));
                return 0;
            }

            // Load messages in reverse order and insert at the beginning
            var insertedCount = 0;
            for (var i = currentStartIndex - 1; i >= newStartIndex; i--)
            {
                var viewModel = ConvertToViewModel(_allMessages[i]);
                if (viewModel != null)
                {
                    Messages.Insert(0, viewModel);
                    insertedCount++;
                }
            }

            _loadedMessageCount += countToLoad;
            _hasMoreMessages = newStartIndex > 0;
            OnPropertyChanged(nameof(HasMoreMessages));

            return insertedCount;
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    /// <summary>
    /// Loads more messages asynchronously, including from disk if messages were unloaded.
    /// </summary>
    /// <returns>The number of messages loaded.</returns>
    public async Task<int> LoadMoreMessagesAsync()
    {
        if (!HasMoreMessages || IsLoadingMore) return 0;

        IsLoadingMore = true;
        try
        {
            // First try loading from _allMessages (in-memory)
            if (_allMessages.Count > 0)
            {
                var totalCount = _allMessages.Count;
                var currentStartIndex = totalCount - _loadedMessageCount;

                if (currentStartIndex > 0)
                {
                    var newStartIndex = Math.Max(0, currentStartIndex - LoadMoreCount);
                    var countToLoad = currentStartIndex - newStartIndex;

                    if (countToLoad > 0)
                    {
                        var insertedCount = 0;
                        for (var i = currentStartIndex - 1; i >= newStartIndex; i--)
                        {
                            var viewModel = ConvertToViewModel(_allMessages[i]);
                            if (viewModel != null)
                            {
                                Messages.Insert(0, viewModel);
                                insertedCount++;
                            }
                        }

                        _loadedMessageCount += countToLoad;
                        _hasMoreMessages = newStartIndex > 0;
                        OnPropertyChanged(nameof(HasMoreMessages));
                        return insertedCount;
                    }
                }
            }

            // If we have unloaded messages on disk, load from there
            if (HasUnloadedMessages && !string.IsNullOrEmpty(_worktreePath) && !string.IsNullOrEmpty(_claudeSessionId))
            {
                return await LoadFromDiskAsync();
            }

            _hasMoreMessages = false;
            OnPropertyChanged(nameof(HasMoreMessages));
            return 0;
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    /// <summary>
    /// Loads messages from disk when they've been unloaded from memory.
    /// </summary>
    private async Task<int> LoadFromDiskAsync()
    {
        if (string.IsNullOrEmpty(_worktreePath) || string.IsNullOrEmpty(_claudeSessionId))
            return 0;

        var historyService = new SessionHistoryService();

        // Calculate range to load
        var startIndex = Math.Max(0, _oldestLoadedIndex - LoadMoreCount);
        var count = _oldestLoadedIndex - startIndex;

        if (count <= 0)
        {
            HasUnloadedMessages = false;
            OnPropertyChanged(nameof(HasMoreMessages));
            return 0;
        }

        // Read messages from disk
        var messages = await historyService.ReadSessionHistoryRangeAsync(
            _worktreePath, _claudeSessionId, startIndex, count);

        // Convert and insert at beginning
        var insertedCount = 0;
        foreach (var msg in messages.Reverse())
        {
            var vm = ConvertHistoryMessageToViewModel(msg);
            if (vm != null)
            {
                Messages.Insert(0, vm);
                insertedCount++;
            }
        }

        _oldestLoadedIndex = startIndex;
        HasUnloadedMessages = startIndex > 0;
        OnPropertyChanged(nameof(UnloadedMessageCount));
        OnPropertyChanged(nameof(HasMoreMessages));

        return insertedCount;
    }

    /// <summary>
    /// Converts a SessionHistoryMessage to a MessageViewModel.
    /// </summary>
    private MessageViewModel? ConvertHistoryMessageToViewModel(SessionHistoryMessage msg)
    {
        if (msg.Role == "user")
        {
            return new UserMessageViewModel { Content = msg.Content };
        }
        else if (msg.Role == "assistant")
        {
            return new AssistantMessageViewModel { TextContent = msg.Content };
        }
        return null;
    }

    /// <summary>
    /// Converts an SDK message to a view model.
    /// </summary>
    private MessageViewModel? ConvertToViewModel(ISDKMessage message)
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

                return userVm;

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
                // Return if there's text content OR tool uses
                if (!string.IsNullOrEmpty(vm.TextContent) || vm.ToolUses.Count > 0)
                {
                    return vm;
                }
                return null;

            default:
                return null;
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
