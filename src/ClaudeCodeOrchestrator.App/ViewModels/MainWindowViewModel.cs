using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeCodeOrchestrator.App.Models;
using ClaudeCodeOrchestrator.App.Services;
using ClaudeCodeOrchestrator.App.ViewModels.Docking;
using ClaudeCodeOrchestrator.App.Views.Docking;
using ClaudeCodeOrchestrator.Core.Services;
using ClaudeCodeOrchestrator.Git.Models;
using ClaudeCodeOrchestrator.Git.Services;
using System.Collections.Concurrent;

namespace ClaudeCodeOrchestrator.App.ViewModels;

/// <summary>
/// Main window view model.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IDialogService _dialogService;
    private readonly IGitService _gitService;
    private readonly IWorktreeService _worktreeService;
    private readonly ISessionService _sessionService;
    private readonly ISettingsService _settingsService;
    private readonly IDispatcher _dispatcher;

    private object? _layout;
    private string? _currentRepositoryPath;
    private string _windowTitle = "Claude Code Orchestrator";
    private bool _isRepositoryOpen;
    private bool _disposed;

    // Track pending preview states for sessions being created
    private readonly ConcurrentDictionary<string, bool> _pendingSessionPreviewStates = new();

    /// <summary>
    /// Reference to DockFactory for dynamic document creation.
    /// </summary>
    public DockFactory? Factory { get; set; }

    public MainWindowViewModel()
    {
        // Get services from locator
        _dialogService = ServiceLocator.GetRequiredService<IDialogService>();
        _gitService = ServiceLocator.GetRequiredService<IGitService>();
        _worktreeService = ServiceLocator.GetRequiredService<IWorktreeService>();
        _sessionService = ServiceLocator.GetRequiredService<ISessionService>();
        _settingsService = ServiceLocator.GetRequiredService<ISettingsService>();
        _dispatcher = ServiceLocator.GetRequiredService<IDispatcher>();

        // Subscribe to session events
        _sessionService.SessionCreated += OnSessionCreated;
        _sessionService.SessionEnded += OnSessionEnded;
        _sessionService.SessionStateChanged += OnSessionStateChanged;
        _sessionService.ClaudeSessionIdReceived += OnClaudeSessionIdReceived;
    }

    /// <summary>
    /// Gets or sets the dock layout (IRootDock).
    /// Using object type to avoid source generator issues with external types.
    /// </summary>
    public object? Layout
    {
        get => _layout;
        set => SetProperty(ref _layout, value);
    }

    public string? CurrentRepositoryPath
    {
        get => _currentRepositoryPath;
        set => SetProperty(ref _currentRepositoryPath, value);
    }

    public string WindowTitle
    {
        get => _windowTitle;
        set => SetProperty(ref _windowTitle, value);
    }

    public bool IsRepositoryOpen
    {
        get => _isRepositoryOpen;
        set => SetProperty(ref _isRepositoryOpen, value);
    }

    public ObservableCollection<SessionViewModel> Sessions { get; } = new();

    public ObservableCollection<WorktreeViewModel> Worktrees { get; } = new();

    /// <summary>
    /// Initializes the view model, restoring last opened repository if valid.
    /// </summary>
    public async Task InitializeAsync()
    {
        var lastPath = _settingsService.LastRepositoryPath;
        if (string.IsNullOrEmpty(lastPath)) return;

        // Check if path still exists and is a git repository
        if (!Directory.Exists(lastPath))
        {
            // Path no longer exists, clear it
            _settingsService.SetLastRepositoryPath(null);
            return;
        }

        try
        {
            // Validate it's still a git repository
            await _gitService.OpenRepositoryAsync(lastPath);

            // Restore the repository
            SetRepository(lastPath);
            await RefreshWorktreesAsync();
            Factory?.UpdateFileBrowser(lastPath);
        }
        catch
        {
            // Not a valid git repository anymore, clear it
            _settingsService.SetLastRepositoryPath(null);
        }
    }

    [RelayCommand]
    private async Task OpenRepositoryAsync()
    {
        try
        {
            var path = await _dialogService.ShowFolderPickerAsync("Select Git Repository");
            if (string.IsNullOrEmpty(path)) return;

            // Validate it's a git repository
            await _gitService.OpenRepositoryAsync(path);

            SetRepository(path);

            // Save as last opened repository
            _settingsService.SetLastRepositoryPath(path);

            // Load worktrees
            await RefreshWorktreesAsync();

            // Update file browser
            Factory?.UpdateFileBrowser(path);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Error Opening Repository",
                $"Failed to open repository: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CreateTaskAsync()
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath))
        {
            await _dialogService.ShowErrorAsync("No Repository",
                "Please open a repository first.");
            return;
        }

        try
        {
            var taskInput = await _dialogService.ShowNewTaskDialogAsync();
            if (taskInput is null) return;

            // Create worktree
            var worktree = await _worktreeService.CreateWorktreeAsync(
                CurrentRepositoryPath,
                taskInput.Text);

            // Add to list
            var vm = WorktreeViewModel.FromModel(worktree);
            SetupWorktreeCallbacks(vm);
            Worktrees.Insert(0, vm);

            // Sync to dock panel
            Factory?.AddWorktree(vm);

            // Create session for the worktree with images
            await CreateSessionForWorktreeAsync(worktree, taskInput);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Error Creating Task",
                $"Failed to create task: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CloseRepository()
    {
        CurrentRepositoryPath = null;
        IsRepositoryOpen = false;
        WindowTitle = "Claude Code Orchestrator";
        Sessions.Clear();
        Worktrees.Clear();
        Factory?.UpdateFileBrowser(null);
        Factory?.UpdateWorktrees(Enumerable.Empty<WorktreeViewModel>());

        // Clear saved repository path
        _settingsService.SetLastRepositoryPath(null);
    }

    public void SetRepository(string path)
    {
        CurrentRepositoryPath = path;
        IsRepositoryOpen = true;
        WindowTitle = $"Claude Code Orchestrator - {Path.GetFileName(path)}";
    }

    public async Task RefreshWorktreesAsync()
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        try
        {
            var worktrees = await _worktreeService.GetWorktreesAsync(CurrentRepositoryPath);

            await _dispatcher.InvokeAsync(() =>
            {
                Worktrees.Clear();
                foreach (var wt in worktrees)
                {
                    var vm = WorktreeViewModel.FromModel(wt);
                    SetupWorktreeCallbacks(vm);
                    Worktrees.Add(vm);
                }

                // Sync to dock panel
                Factory?.UpdateWorktrees(Worktrees);
            });
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Error Loading Worktrees",
                $"Failed to load worktrees: {ex.Message}");
        }
    }

    private void SetupWorktreeCallbacks(WorktreeViewModel vm)
    {
        vm.OnOpenSessionRequested = OnOpenSessionRequestedAsync;
        vm.OnMergeRequested = OnMergeRequestedAsync;
        vm.OnDeleteRequested = OnDeleteRequestedAsync;
    }

    private async Task OnOpenSessionRequestedAsync(WorktreeViewModel worktree)
    {
        // Use the same logic as OpenWorktreeSessionAsync to load history
        // Open as persistent (not preview) since user explicitly clicked the open button
        await OpenWorktreeSessionAsync(worktree, isPreview: false);
    }

    private async Task OnOpenSessionRequestedAsync_Legacy(WorktreeViewModel worktree)
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        try
        {
            // Get the WorktreeInfo to create a session
            var worktreeInfo = await _worktreeService.GetWorktreeAsync(
                CurrentRepositoryPath, worktree.Id);

            if (worktreeInfo is null) return;

            // Check if session already exists for this worktree
            if (worktree.HasActiveSession && !string.IsNullOrEmpty(worktree.ActiveSessionId))
            {
                // Activate existing session document
                Factory?.ActivateSessionDocument(worktree.ActiveSessionId);
                return;
            }

            // Create new session with continuation prompt
            await CreateSessionForWorktreeAsync(worktreeInfo, "Continue working on this task.");
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Error Opening Session",
                $"Failed to open session: {ex.Message}");
        }
    }

    private async Task OnMergeRequestedAsync(WorktreeViewModel worktree)
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        try
        {
            var confirmed = await _dialogService.ShowConfirmAsync("Merge Worktree",
                $"Are you sure you want to merge '{worktree.BranchName}' into '{worktree.BaseBranch}'?");

            if (!confirmed) return;

            var result = await _worktreeService.MergeWorktreeAsync(
                CurrentRepositoryPath,
                worktree.Id,
                worktree.BaseBranch);

            if (result.Success)
            {
                await CompleteMergeAsync(worktree);
            }
            else if (result.Status == MergeStatus.Conflicts)
            {
                // Ask user if they want Claude to resolve conflicts
                var resolveConflicts = await _dialogService.ShowConfirmAsync("Merge Conflicts",
                    $"Merge conflicts detected in:\n{string.Join("\n", result.ConflictingFiles ?? [])}\n\n" +
                    "Would you like Claude to resolve these conflicts?");

                if (resolveConflicts)
                {
                    await ResolveConflictsWithClaudeAsync(worktree, result.ConflictingFiles ?? []);
                }
                else
                {
                    await _dialogService.ShowErrorAsync("Merge Failed",
                        $"Merge conflicts in: {string.Join(", ", result.ConflictingFiles ?? [])}");
                }
            }
            else
            {
                await _dialogService.ShowErrorAsync("Merge Failed",
                    result.ErrorMessage ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Error Merging",
                $"Failed to merge worktree: {ex.Message}");
        }
    }

    private async Task CompleteMergeAsync(WorktreeViewModel worktree)
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        // Close any open session documents for this worktree
        Factory?.RemoveSessionDocumentsByWorktree(worktree.Id);

        // Delete the worktree since merge is complete
        await _worktreeService.DeleteWorktreeAsync(
            CurrentRepositoryPath,
            worktree.Id,
            force: true);

        Worktrees.Remove(worktree);
        Factory?.RemoveWorktree(worktree);
    }

    private async Task ResolveConflictsWithClaudeAsync(WorktreeViewModel worktree, IReadOnlyList<string> conflictingFiles)
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        var worktreeInfo = await _worktreeService.GetWorktreeAsync(CurrentRepositoryPath, worktree.Id);
        if (worktreeInfo == null) return;

        // Claude works in the worktree, so instruct it to:
        // 1. Fetch latest changes
        // 2. Merge the base branch into the current worktree branch
        // 3. Resolve conflicts
        // 4. Commit the merge
        var conflictPrompt = $"""
            Please merge the latest changes from '{worktree.BaseBranch}' into the current branch.

            Run these commands:
            1. git fetch origin
            2. git merge origin/{worktree.BaseBranch}

            When merge conflicts occur, resolve them in the following files: {string.Join(", ", conflictingFiles)}

            After resolving all conflicts, stage the resolved files and commit the merge.
            """;

        // Find existing session for this worktree by worktree ID (more reliable than ActiveSessionId
        // which gets cleared when session ends)
        var existingSession = _sessionService.GetSessionByWorktreeId(worktree.Id);

        string sessionId;
        if (existingSession != null)
        {
            sessionId = existingSession.Id;

            // Add user message to UI before sending
            var sessionDoc = Factory?.GetSessionDocument(sessionId);
            sessionDoc?.AddExternalUserMessage(conflictPrompt);

            // Send message to existing session - will resume if completed, queue if processing
            await _sessionService.SendMessageAsync(sessionId, conflictPrompt);

            // Update UI to show session is active again
            worktree.HasActiveSession = true;
            worktree.ActiveSessionId = sessionId;

            // Activate the session document so user can watch the merge
            Factory?.ActivateSessionDocument(sessionId);
        }
        else
        {
            // No existing session - create a new one with the conflict resolution prompt
            // This will trigger OnSessionCreated which creates the session document
            var session = await _sessionService.CreateSessionAsync(worktreeInfo, conflictPrompt);
            sessionId = session.Id;

            // Update worktree with active session
            worktree.HasActiveSession = true;
            worktree.ActiveSessionId = sessionId;
            // New session - document will be created and activated by OnSessionCreated
        }

        // Store the worktree info for retry after session completes
        _pendingMergeRetries[sessionId] = worktree;
    }

    // Track pending merge retries by session ID
    private readonly ConcurrentDictionary<string, WorktreeViewModel> _pendingMergeRetries = new();

    private async Task OnSessionEndedForMergeRetryAsync(string sessionId, Core.Models.SessionState finalState)
    {
        if (!_pendingMergeRetries.TryRemove(sessionId, out var worktree)) return;

        if (string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        // Only retry if session completed successfully
        if (finalState != Core.Models.SessionState.Completed)
        {
            await _dialogService.ShowErrorAsync("Merge Failed",
                "Claude could not resolve the merge conflicts. Please resolve them manually.");
            // Refresh worktrees to show updated status
            await RefreshWorktreesAsync();
            return;
        }

        // Retry the merge
        var result = await _worktreeService.MergeWorktreeAsync(
            CurrentRepositoryPath,
            worktree.Id,
            worktree.BaseBranch);

        if (result.Success)
        {
            await CompleteMergeAsync(worktree);
            // Refresh worktrees after successful merge
            await RefreshWorktreesAsync();
        }
        else
        {
            var message = result.ConflictingFiles?.Count > 0
                ? $"Merge still has conflicts in: {string.Join(", ", result.ConflictingFiles)}"
                : result.ErrorMessage ?? "Unknown error";

            await _dialogService.ShowErrorAsync("Merge Failed", message);
            // Refresh worktrees to show updated status
            await RefreshWorktreesAsync();
        }
    }

    private async Task OnDeleteRequestedAsync(WorktreeViewModel worktree)
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        try
        {
            var confirmed = await _dialogService.ShowConfirmAsync("Delete Worktree",
                $"Are you sure you want to delete worktree '{worktree.BranchName}'?\n\nThis action cannot be undone.");

            if (!confirmed) return;

            await _worktreeService.DeleteWorktreeAsync(
                CurrentRepositoryPath,
                worktree.Id,
                force: true);

            // Close any open session documents for this worktree
            Factory?.RemoveSessionDocumentsByWorktree(worktree.Id);

            Worktrees.Remove(worktree);

            // Sync to dock panel
            Factory?.RemoveWorktree(worktree);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Error Deleting",
                $"Failed to delete worktree: {ex.Message}");
        }
    }

    private async Task CreateSessionForWorktreeAsync(WorktreeInfo worktree, string prompt, bool isPreview = false)
    {
        await CreateSessionForWorktreeAsync(worktree, TaskInput.FromText(prompt), isPreview);
    }

    private async Task CreateSessionForWorktreeAsync(WorktreeInfo worktree, TaskInput taskInput, bool isPreview = false)
    {
        try
        {
            // Store the preview state for when the session is created
            _pendingSessionPreviewStates[worktree.Id] = isPreview;

            var session = await _sessionService.CreateSessionAsync(worktree, taskInput.Text, taskInput.Images);

            // Mark the worktree as having an active session
            var worktreeVm = Worktrees.FirstOrDefault(w => w.Id == worktree.Id);
            if (worktreeVm != null)
            {
                worktreeVm.HasActiveSession = true;
                worktreeVm.ActiveSessionId = session.Id;
            }
        }
        catch (Exception ex)
        {
            _pendingSessionPreviewStates.TryRemove(worktree.Id, out _);
            await _dialogService.ShowErrorAsync("Error Creating Session",
                $"Failed to create session: {ex.Message}");
        }
    }

    private void OnSessionCreated(object? sender, SessionCreatedEventArgs e)
    {
        _dispatcher.Post(() =>
        {
            // Create new document for session
            var branch = GetWorktreeBranch(e.Session.WorktreeId);
            var document = new SessionDocumentViewModel(
                e.Session.Id,
                e.Session.Title,
                branch,
                e.Session.WorktreeId);

            // Load any existing messages from the session (for resumed sessions)
            document.LoadMessagesFromSession(e.Session);

            // Check if this session should be opened as preview
            var isPreview = _pendingSessionPreviewStates.TryRemove(e.Session.WorktreeId, out var preview) && preview;

            // Add to document dock via factory
            Factory?.AddSessionDocument(document, isPreview);
        });
    }

    private void OnSessionEnded(object? sender, SessionEndedEventArgs e)
    {
        _dispatcher.Post(() =>
        {
            // Update worktree to show no active session
            var worktree = Worktrees.FirstOrDefault(w =>
                w.ActiveSessionId == e.SessionId);

            if (worktree != null)
            {
                worktree.HasActiveSession = false;
                worktree.ActiveSessionId = null;
            }

            // Check if this session has a pending merge retry
            // Fire and forget with proper error handling
            _ = SafeRetryMergeAsync(e.SessionId, e.FinalState);
        });
    }

    private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        _dispatcher.Post(() =>
        {
            // When a session transitions to an active state (Processing, Active, Starting),
            // ensure the worktree shows it as having an active session.
            // This is needed when a completed session is resumed with a follow-up message.
            if (e.Session.State is Core.Models.SessionState.Processing
                or Core.Models.SessionState.Active
                or Core.Models.SessionState.Starting)
            {
                var worktree = Worktrees.FirstOrDefault(w => w.Id == e.Session.WorktreeId);
                if (worktree != null && !worktree.HasActiveSession)
                {
                    worktree.HasActiveSession = true;
                    worktree.ActiveSessionId = e.Session.Id;
                }
            }
        });
    }

    private async Task SafeRetryMergeAsync(string sessionId, Core.Models.SessionState finalState)
    {
        try
        {
            await OnSessionEndedForMergeRetryAsync(sessionId, finalState);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Error", $"Failed to retry merge: {ex.Message}");
        }
    }

    private async void OnClaudeSessionIdReceived(object? sender, ClaudeSessionIdReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        try
        {
            // Find the worktree by ID
            var worktree = await _worktreeService.GetWorktreeAsync(CurrentRepositoryPath, e.WorktreeId);
            if (worktree == null) return;

            // Save the Claude session ID to the worktree metadata
            await _worktreeService.UpdateClaudeSessionIdAsync(worktree.Path, e.ClaudeSessionId);
        }
        catch
        {
            // Ignore errors saving session ID - it's not critical
        }
    }

    private string GetWorktreeBranch(string worktreeId)
    {
        return Worktrees.FirstOrDefault(w => w.Id == worktreeId)?.BranchName ?? "unknown";
    }

    /// <summary>
    /// Opens a session for a worktree when clicked in the worktrees panel.
    /// </summary>
    /// <param name="worktree">The worktree to open a session for.</param>
    /// <param name="isPreview">If true, opens as preview (single-click). If false, opens as persistent (double-click).</param>
    public async Task OpenWorktreeSessionAsync(WorktreeViewModel worktree, bool isPreview = true)
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        try
        {
            // Check if session already exists for this worktree in memory
            if (worktree.HasActiveSession && !string.IsNullOrEmpty(worktree.ActiveSessionId))
            {
                // If opening as persistent (double-click), promote any existing preview
                if (!isPreview)
                {
                    Factory?.PromoteSessionPreviewDocument(worktree.ActiveSessionId);
                }

                // Activate existing session document
                Factory?.ActivateSessionDocument(worktree.ActiveSessionId);
                return;
            }

            // Store the preview state for when the session is created
            _pendingSessionPreviewStates[worktree.Id] = isPreview;

            // Get the WorktreeInfo
            var worktreeInfo = await _worktreeService.GetWorktreeAsync(
                CurrentRepositoryPath, worktree.Id);

            if (worktreeInfo is null)
            {
                _pendingSessionPreviewStates.TryRemove(worktree.Id, out _);
                return;
            }

            // Use the worktree's stored Claude session ID, or find one from disk
            var claudeSessionId = worktreeInfo.ClaudeSessionId;
            if (string.IsNullOrEmpty(claudeSessionId))
            {
                // Fallback: search for existing sessions on disk
                var historyService = new SessionHistoryService();
                claudeSessionId = historyService.GetMostRecentSession(worktreeInfo.Path);
            }

            if (!string.IsNullOrEmpty(claudeSessionId))
            {
                // Create an idle session that will resume from the existing Claude session
                await CreateIdleSessionWithHistoryAsync(worktreeInfo, claudeSessionId);
            }
            else
            {
                // No existing session - create an idle session waiting for user input
                await _sessionService.CreateIdleSessionAsync(worktreeInfo);
            }
        }
        catch (Exception ex)
        {
            _pendingSessionPreviewStates.TryRemove(worktree.Id, out _);
            await _dialogService.ShowErrorAsync("Error Opening Session",
                $"Failed to open session: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates an idle session and loads history from an existing Claude session.
    /// </summary>
    private async Task CreateIdleSessionWithHistoryAsync(WorktreeInfo worktree, string claudeSessionId)
    {
        // Load the history FIRST before creating the session
        var historyService = new SessionHistoryService();
        var history = await historyService.ReadSessionHistoryAsync(worktree.Path, claudeSessionId);

        // Convert history to SDK messages
        var historyMessages = new List<SDK.Messages.ISDKMessage>();
        foreach (var msg in history)
        {
            if (msg.Role == "user")
            {
                historyMessages.Add(SDK.Messages.SDKUserMessage.CreateText(msg.Content, claudeSessionId));
            }
            else if (msg.Role == "assistant")
            {
                // Build content blocks including text and tool uses
                var contentBlocks = new List<SDK.Messages.ContentBlock>();

                // Add text content if present
                if (!string.IsNullOrEmpty(msg.Content))
                {
                    contentBlocks.Add(new SDK.Messages.TextContentBlock { Text = msg.Content });
                }

                // Add tool use blocks from history
                foreach (var toolUse in msg.ToolUses)
                {
                    // Parse the JSON input - the Input property accepts object and ToString() is called on it
                    var inputElement = System.Text.Json.JsonDocument.Parse(toolUse.InputJson).RootElement;
                    contentBlocks.Add(new SDK.Messages.ToolUseContentBlock
                    {
                        Id = toolUse.Id,
                        Name = toolUse.Name,
                        Input = inputElement
                    });
                }

                historyMessages.Add(new SDK.Messages.SDKAssistantMessage
                {
                    Uuid = msg.Uuid ?? Guid.NewGuid().ToString(),
                    SessionId = claudeSessionId,
                    Message = new SDK.Messages.AssistantMessageContent
                    {
                        Id = msg.Uuid ?? Guid.NewGuid().ToString(),
                        Model = "claude-opus-4-5-20251101",
                        Content = contentBlocks
                    }
                });
            }
        }

        // Create options to resume the existing session when user sends a message
        var options = new SDK.Options.ClaudeAgentOptions
        {
            Resume = claudeSessionId
        };

        // Create session with history messages already populated
        var session = await _sessionService.CreateIdleSessionAsync(worktree, options, historyMessages);

        // Store the Claude session ID for resumption
        session.ClaudeSessionId = claudeSessionId;
    }

    /// <summary>
    /// Opens a file document when clicked in the file browser.
    /// </summary>
    /// <param name="filePath">The path to the file to open.</param>
    /// <param name="isPreview">If true, opens as a preview tab (replaced on next single-click).</param>
    public Task OpenFileDocumentAsync(string filePath, bool isPreview)
    {
        try
        {
            var document = new FileDocumentViewModel(filePath);
            Factory?.AddFileDocument(document, isPreview);
        }
        catch (Exception ex)
        {
            _dialogService.ShowErrorAsync("Error Opening File",
                $"Failed to open file: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _sessionService.SessionCreated -= OnSessionCreated;
        _sessionService.SessionEnded -= OnSessionEnded;
        _sessionService.SessionStateChanged -= OnSessionStateChanged;
        _sessionService.ClaudeSessionIdReceived -= OnClaudeSessionIdReceived;
    }
}
