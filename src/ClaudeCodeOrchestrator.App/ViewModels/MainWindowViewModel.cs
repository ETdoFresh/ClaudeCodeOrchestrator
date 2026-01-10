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
    private readonly IRepositorySettingsService _repositorySettingsService;
    private readonly IDispatcher _dispatcher;
    private readonly ITitleGeneratorService _titleGeneratorService;

    private object? _layout;
    private string? _currentRepositoryPath;
    private string _windowTitle = "Claude Code Orchestrator";
    private bool _isRepositoryOpen;
    private bool _disposed;
    private string? _autoSplitLayoutText;
    private string? _gitHubUrl;

    // Track pending preview states for sessions being created
    private readonly ConcurrentDictionary<string, bool> _pendingSessionPreviewStates = new();

    // Track pending accumulated durations for sessions being restored with history
    private readonly ConcurrentDictionary<string, TimeSpan> _pendingAccumulatedDurations = new();

    // Track worktrees currently having sessions opened (prevents duplicate tabs from race conditions)
    private readonly ConcurrentDictionary<string, byte> _worktreesOpeningSession = new();

    /// <summary>
    /// Reference to DockFactory for dynamic document creation.
    /// </summary>
    public DockFactory? Factory
    {
        get => _factory;
        set
        {
            // Unsubscribe from old factory
            if (_factory != null)
            {
                _factory.AutoSplitLayoutChanged -= OnAutoSplitLayoutChanged;
                _factory.SessionDocumentClosed -= OnSessionDocumentClosed;
            }

            _factory = value;

            // Subscribe to new factory
            if (_factory != null)
            {
                _factory.AutoSplitLayoutChanged += OnAutoSplitLayoutChanged;
                _factory.SessionDocumentClosed += OnSessionDocumentClosed;
                UpdateAutoSplitLayoutText(_factory.AutoSplitLayout);
            }
        }
    }
    private DockFactory? _factory;

    public MainWindowViewModel()
    {
        // Get services from locator
        _dialogService = ServiceLocator.GetRequiredService<IDialogService>();
        _gitService = ServiceLocator.GetRequiredService<IGitService>();
        _worktreeService = ServiceLocator.GetRequiredService<IWorktreeService>();
        _sessionService = ServiceLocator.GetRequiredService<ISessionService>();
        _settingsService = ServiceLocator.GetRequiredService<ISettingsService>();
        _repositorySettingsService = ServiceLocator.GetRequiredService<IRepositorySettingsService>();
        _dispatcher = ServiceLocator.GetRequiredService<IDispatcher>();
        _titleGeneratorService = ServiceLocator.GetRequiredService<ITitleGeneratorService>();

        // Subscribe to repository settings changes to update CanRun on worktrees
        _repositorySettingsService.SettingsChanged += OnRepositorySettingsChanged;

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

    /// <summary>
    /// Gets the display text for the current auto-split layout mode.
    /// Returns null when auto-split is disabled.
    /// </summary>
    public string? AutoSplitLayoutText
    {
        get => _autoSplitLayoutText;
        private set => SetProperty(ref _autoSplitLayoutText, value);
    }

    /// <summary>
    /// Gets the GitHub URL for the current repository, if it's a GitHub repository.
    /// Returns null if no repository is open or the remote is not GitHub.
    /// </summary>
    public string? GitHubUrl
    {
        get => _gitHubUrl;
        private set => SetProperty(ref _gitHubUrl, value);
    }

    /// <summary>
    /// Gets whether the run button for the main repository should be visible.
    /// True when a repository is open and an executable is configured.
    /// </summary>
    public bool CanRunMainRepo => IsRepositoryOpen && _repositorySettingsService.HasExecutable;

    public ObservableCollection<SessionViewModel> Sessions { get; } = new();

    public ObservableCollection<WorktreeViewModel> Worktrees { get; } = new();

    public ObservableCollection<RecentRepositoryItem> RecentRepositories { get; } = new();

    /// <summary>
    /// Gets whether there are any recent repositories to display.
    /// </summary>
    public bool HasRecentRepositories => RecentRepositories.Count > 0;

    /// <summary>
    /// Initializes the view model, restoring last opened repository if valid.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Load recent repositories
        RefreshRecentRepositories();

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
            await OpenRepositoryAtPathAsync(lastPath);

            // Reconnect to worktrees that had active sessions when app closed
            await ReconnectActiveSessionsAsync();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No Git repository found"))
        {
            // User declined to initialize git - clear the saved path
            _settingsService.SetLastRepositoryPath(null);
        }
        catch
        {
            // Other error - clear it
            _settingsService.SetLastRepositoryPath(null);
        }
    }

    /// <summary>
    /// Reconnects to worktrees that had active sessions when the app was last closed.
    /// </summary>
    private async Task ReconnectActiveSessionsAsync()
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        var worktreesToReconnect = new List<WorktreeViewModel>();

        // Find worktrees that had active sessions
        foreach (var worktree in Worktrees)
        {
            var worktreeInfo = await _worktreeService.GetWorktreeAsync(CurrentRepositoryPath, worktree.Id);
            if (worktreeInfo?.SessionWasActive == true && !string.IsNullOrEmpty(worktreeInfo.ClaudeSessionId))
            {
                worktreesToReconnect.Add(worktree);
            }
        }

        // Reconnect each worktree with an active session
        foreach (var worktree in worktreesToReconnect)
        {
            try
            {
                // Open as non-preview since these were active sessions
                await OpenWorktreeSessionAsync(worktree, isPreview: false);
            }
            catch
            {
                // Ignore errors reconnecting individual sessions
            }
        }
    }

    [RelayCommand]
    private async Task OpenRepositoryAsync()
    {
        try
        {
            var path = await _dialogService.ShowFolderPickerAsync("Select Git Repository");
            if (string.IsNullOrEmpty(path)) return;

            await OpenRepositoryAtPathAsync(path);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No Git repository found"))
        {
            // Don't show error - it was handled in OpenRepositoryAtPathAsync
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Error Opening Repository",
                $"Failed to open repository: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OpenRecentRepositoryAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            await OpenRepositoryAtPathAsync(path);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No Git repository found"))
        {
            // Don't show error - it was handled in OpenRepositoryAtPathAsync
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Error Opening Repository",
                $"Failed to open repository: {ex.Message}");
        }
    }

    private void RefreshRecentRepositories()
    {
        RecentRepositories.Clear();
        foreach (var path in _settingsService.RecentRepositories)
        {
            // Only include paths that still exist
            if (Directory.Exists(path))
            {
                RecentRepositories.Add(new RecentRepositoryItem(path, OpenRecentRepositoryCommand));
            }
        }
        OnPropertyChanged(nameof(HasRecentRepositories));
    }

    internal async Task OpenRepositoryAtPathAsync(string path)
    {
        // Close the current repository first if one is open (handles switching repositories)
        if (IsRepositoryOpen)
        {
            await CloseRepositoryAsync();
        }

        try
        {
            // Validate it's a git repository
            await _gitService.OpenRepositoryAsync(path);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No Git repository found"))
        {
            // Not a git repository - ask user if they want to initialize one
            var initialize = await _dialogService.ShowConfirmAsync(
                "Initialize Git Repository",
                $"The selected directory is not a Git repository.\n\nWould you like to initialize a new Git repository at:\n{path}");

            if (!initialize)
            {
                throw; // Re-throw to prevent further processing
            }

            // Initialize the repository
            await _gitService.InitializeRepositoryAsync(path);
        }

        SetRepository(path);

        // Load per-repository settings
        _repositorySettingsService.Load(path);

        // Save as last opened repository and add to recent repositories
        _settingsService.SetLastRepositoryPath(path);
        _settingsService.AddRecentRepository(path);
        RefreshRecentRepositories();

        // Fetch remote URL and set GitHubUrl if it's a GitHub repository
        await UpdateGitHubUrlAsync(path);

        // Load worktrees
        await RefreshWorktreesAsync();

        // Update file browser
        Factory?.UpdateFileBrowser(path);
    }

    private async Task UpdateGitHubUrlAsync(string path)
    {
        try
        {
            var remoteUrl = await _gitService.GetRemoteUrlAsync(path);
            GitHubUrl = ConvertToGitHubUrl(remoteUrl);
        }
        catch
        {
            GitHubUrl = null;
        }
    }

    private static string? ConvertToGitHubUrl(string? remoteUrl)
    {
        if (string.IsNullOrEmpty(remoteUrl))
            return null;

        // Handle SSH URLs: git@github.com:user/repo.git
        if (remoteUrl.StartsWith("git@github.com:"))
        {
            var path = remoteUrl["git@github.com:".Length..];
            if (path.EndsWith(".git"))
                path = path[..^4];
            return $"https://github.com/{path}";
        }

        // Handle HTTPS URLs: https://github.com/user/repo.git
        if (remoteUrl.StartsWith("https://github.com/") || remoteUrl.StartsWith("http://github.com/"))
        {
            var url = remoteUrl;
            if (url.EndsWith(".git"))
                url = url[..^4];
            // Ensure it uses https
            if (url.StartsWith("http://"))
                url = "https://" + url[7..];
            return url;
        }

        // Not a GitHub URL
        return null;
    }

    /// <summary>
    /// Creates a task from the provided input (called from inline input control).
    /// Title and branch name will be generated automatically.
    /// </summary>
    public async Task CreateTaskFromInputAsync(TaskInput taskInput)
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath))
        {
            throw new InvalidOperationException("Please open a repository first.");
        }

        // Generate title and branch name
        var generated = await _titleGeneratorService.GenerateTitleAsync(taskInput.Text);
        var title = generated.Title;
        var branchName = generated.BranchName;

        // Create worktree with the generated title and branch name
        var worktree = await _worktreeService.CreateWorktreeAsync(
            CurrentRepositoryPath,
            taskInput.Text,
            title: title,
            branchName: branchName);

        // Add to list
        var vm = WorktreeViewModel.FromModel(worktree);
        SetupWorktreeCallbacks(vm);
        Worktrees.Insert(0, vm);

        // Sync to dock panel
        Factory?.AddWorktree(vm);

        // Create session for the worktree with images
        // Create a new TaskInput with the generated title/branch
        var fullTaskInput = TaskInput.Create(
            taskInput.Text,
            taskInput.Images.ToList(),
            title,
            branchName);
        await CreateSessionForWorktreeAsync(worktree, fullTaskInput);
    }

    [RelayCommand]
    private async Task RefreshWorktrees()
    {
        await RefreshWorktreesAsync();
    }

    [RelayCommand]
    private async Task PushAllBranchesAsync()
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath))
        {
            await _dialogService.ShowErrorAsync("No Repository",
                "Please open a repository first.");
            return;
        }

        try
        {
            await _gitService.PushAllBranchesAsync(CurrentRepositoryPath);
            // Refresh to update the badge count after successful push
            await RefreshWorktreesAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Push Failed",
                $"Failed to push branches: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task PullAsync()
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath))
        {
            await _dialogService.ShowErrorAsync("No Repository",
                "Please open a repository first.");
            return;
        }

        try
        {
            await _gitService.PullAsync(CurrentRepositoryPath);
            // Refresh to update worktrees after pull
            await RefreshWorktreesAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Pull Failed",
                $"Failed to pull changes: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CloseRepositoryAsync()
    {
        // End all active sessions (kills all Claude Code subprocesses)
        await _sessionService.EndAllSessionsAsync();

        // Close all document tabs (disposes session documents, file documents, etc.)
        Factory?.CloseAllDocuments();

        // Dispose and clear worktrees (stops timers, releases resources)
        foreach (var worktree in Worktrees)
        {
            worktree.Dispose();
        }

        // Clear collections
        Sessions.Clear();
        Worktrees.Clear();

        // Clear UI state
        CurrentRepositoryPath = null;
        IsRepositoryOpen = false;
        WindowTitle = "Claude Code Orchestrator";
        GitHubUrl = null;

        // Clear per-repository settings
        _repositorySettingsService.Clear();

        // Update file browser to clear file list
        Factory?.UpdateFileBrowser(null);

        // Clear worktrees panel
        Factory?.ClearWorktrees();

        // Clear saved repository path
        _settingsService.SetLastRepositoryPath(null);

        // Clear any pending merge retries
        _pendingMergeRetries.Clear();

        // Clear pending session preview states
        _pendingSessionPreviewStates.Clear();

        // Clear pending accumulated durations
        _pendingAccumulatedDurations.Clear();

        // Clear worktrees opening session lock
        _worktreesOpeningSession.Clear();

        // Notify CanRunMainRepo since IsRepositoryOpen changed
        OnPropertyChanged(nameof(CanRunMainRepo));
    }

    [RelayCommand]
    private void SplitVertical()
    {
        Factory?.SplitAllDocuments(Views.Docking.SplitLayout.Vertical);
    }

    [RelayCommand]
    private void SplitHorizontal()
    {
        Factory?.SplitAllDocuments(Views.Docking.SplitLayout.Horizontal);
    }

    [RelayCommand]
    private void SplitGrid()
    {
        Factory?.SplitAllDocuments(Views.Docking.SplitLayout.Grid);
    }

    [RelayCommand]
    private void CollapseSplit()
    {
        Factory?.CollapseSplitDocuments();
    }

    [RelayCommand]
    private void AutoSplitVertical()
    {
        Factory?.EnableAutoSplit(Views.Docking.SplitLayout.Vertical);
    }

    [RelayCommand]
    private void AutoSplitHorizontal()
    {
        Factory?.EnableAutoSplit(Views.Docking.SplitLayout.Horizontal);
    }

    [RelayCommand]
    private void AutoSplitGrid()
    {
        Factory?.EnableAutoSplit(Views.Docking.SplitLayout.Grid);
    }

    [RelayCommand]
    private void DisableAutoSplit()
    {
        Factory?.DisableAutoSplit();
    }

    [RelayCommand]
    private void ResetView()
    {
        Factory?.ResetToDefaultView();
    }

    [RelayCommand]
    private async Task ShowAboutAsync()
    {
        await _dialogService.ShowAboutAsync();
    }

    [RelayCommand]
    private void OpenUsage()
    {
        var url = "https://claude.ai/settings/usage";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Silently fail if browser can't be opened
        }
    }

    [RelayCommand]
    private void OpenRepo()
    {
        if (string.IsNullOrEmpty(GitHubUrl)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = GitHubUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // Silently fail if browser can't be opened
        }
    }

    [RelayCommand]
    private async Task OpenRepositorySettingsAsync()
    {
        var result = await _dialogService.ShowRepositorySettingsAsync(_repositorySettingsService.Settings?.Executable);
        if (result != null)
        {
            _repositorySettingsService.SetExecutable(result);
        }
    }

    [RelayCommand]
    private async Task RunMainRepoAsync()
    {
        if (!_repositorySettingsService.HasExecutable || string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        try
        {
            var success = await _repositorySettingsService.RunExecutableAsync(CurrentRepositoryPath);
            if (!success)
            {
                await _dialogService.ShowErrorAsync("Run Failed",
                    "Failed to run the configured executable. Check the executable path in Repository Settings.");
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Run Failed",
                $"Failed to run executable: {ex.Message}");
        }
    }

    public void SetRepository(string path)
    {
        CurrentRepositoryPath = path;
        IsRepositoryOpen = true;
        // TrimEnd to handle paths with trailing slashes
        var repoName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        WindowTitle = $"Claude Code Orchestrator - {repoName}";

        // Notify CanRunMainRepo since IsRepositoryOpen changed
        OnPropertyChanged(nameof(CanRunMainRepo));
    }

    public async Task RefreshWorktreesAsync()
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        try
        {
            var worktrees = await _worktreeService.GetWorktreesAsync(CurrentRepositoryPath);

            // Get unpushed commits for the main repository (for Push button badge)
            var mainBranch = await _gitService.GetCurrentBranchAsync(CurrentRepositoryPath);
            var mainRepoUnpushedCommits = await _gitService.GetCommitsAheadOfRemoteAsync(
                CurrentRepositoryPath, mainBranch);

            // Get commits behind remote for the main repository (for Pull button badge)
            var mainRepoCommitsToPull = await _gitService.GetCommitsBehindRemoteAsync(
                CurrentRepositoryPath, mainBranch);

            // Check if the repository has a remote configured
            var hasRemote = await _gitService.HasRemoteAsync(CurrentRepositoryPath);

            await _dispatcher.InvokeAsync(() =>
            {
                // Preserve active session state from existing worktrees
                var activeSessionState = Worktrees
                    .Where(w => w.HasActiveSession)
                    .ToDictionary(
                        w => w.Path,
                        w => (w.ActiveSessionId, w.AccumulatedDuration, w.SessionDuration, w.IsProcessing, w.IsTurnActive));

                Worktrees.Clear();
                foreach (var wt in worktrees)
                {
                    var vm = WorktreeViewModel.FromModel(wt);
                    SetupWorktreeCallbacks(vm);

                    // Restore active session state if this worktree had an active session
                    if (activeSessionState.TryGetValue(vm.Path, out var sessionState))
                    {
                        vm.HasActiveSession = true;
                        vm.ActiveSessionId = sessionState.ActiveSessionId;
                        vm.AccumulatedDuration = sessionState.AccumulatedDuration;
                        vm.SessionDuration = sessionState.SessionDuration;
                        vm.IsProcessing = sessionState.IsProcessing;

                        // Restart the turn timer if a turn was active
                        if (sessionState.IsTurnActive)
                        {
                            vm.StartTurn();
                        }
                    }

                    Worktrees.Add(vm);
                }

                // Sync to dock panel with main repo's unpushed count for badge, remote status, and pull count
                Factory?.UpdateWorktrees(Worktrees, mainRepoUnpushedCommits, hasRemote, mainRepoCommitsToPull);

                // Update merge state on any open session documents
                Factory?.UpdateSessionDocumentsMergeState(Worktrees);
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
        vm.OnRunRequested = OnRunRequestedAsync;
        vm.CanRun = _repositorySettingsService.HasExecutable;
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
            // Check if confirmation is enabled in settings
            if (_settingsService.ShowMergeConfirmation)
            {
                var confirmed = await _dialogService.ShowConfirmAsync("Merge Worktree",
                    $"Are you sure you want to merge '{worktree.BranchName}' into '{worktree.BaseBranch}'?");

                if (!confirmed) return;
            }

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

    /// <summary>
    /// Merges a worktree by its ID. Called from SessionDocumentViewModel.
    /// </summary>
    public async Task MergeWorktreeByIdAsync(string worktreeId)
    {
        var worktree = Worktrees.FirstOrDefault(w => w.Id == worktreeId);
        if (worktree != null)
        {
            await OnMergeRequestedAsync(worktree);
        }
    }

    private async Task CompleteMergeAsync(WorktreeViewModel worktree)
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        // Close any open session documents for this worktree
        Factory?.RemoveSessionDocumentsByWorktree(worktree.Id);

        // Close any open file documents from this worktree's path
        Factory?.RemoveFileDocumentsByWorktreePath(worktree.Path);

        // Delete the worktree since merge is complete
        await _worktreeService.DeleteWorktreeAsync(
            CurrentRepositoryPath,
            worktree.Id,
            force: true);

        Worktrees.Remove(worktree);
        Factory?.RemoveWorktree(worktree);

        // Refresh to update the push badge count after merge adds commits
        await RefreshWorktreesAsync();
    }

    private async Task ResolveConflictsWithClaudeAsync(WorktreeViewModel worktree, IReadOnlyList<string> conflictingFiles)
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        var worktreeInfo = await _worktreeService.GetWorktreeAsync(CurrentRepositoryPath, worktree.Id);
        if (worktreeInfo == null) return;

        // Claude works in the worktree, so instruct it to:
        // 1. Merge the base branch into the current worktree branch
        // 2. Resolve conflicts
        // 3. Commit the merge
        // Note: We merge the local branch directly (no fetch needed) since worktrees share
        // the same git repository and have access to all local branches.
        var conflictPrompt = $"""
            Please merge the latest changes from '{worktree.BaseBranch}' into the current branch.

            Run this command:
            git merge {worktree.BaseBranch}

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
            worktree.IsProcessing = true;

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
            worktree.IsProcessing = true;
            // New session - document will be created and activated by OnSessionCreated
        }

        // Store the worktree info for retry after session completes
        _pendingMergeRetries[sessionId] = worktree;

        // Disable the merge button while Claude is resolving conflicts
        worktree.IsMergePending = true;
    }

    // Track pending merge retries by session ID
    private readonly ConcurrentDictionary<string, WorktreeViewModel> _pendingMergeRetries = new();

    private async Task OnSessionEndedForMergeRetryAsync(string sessionId, Core.Models.SessionState finalState)
    {
        if (!_pendingMergeRetries.TryRemove(sessionId, out var worktree)) return;

        if (string.IsNullOrEmpty(CurrentRepositoryPath))
        {
            // Re-enable merge button even if we can't proceed
            worktree.IsMergePending = false;
            return;
        }

        // Only retry if session completed successfully
        if (finalState != Core.Models.SessionState.Completed)
        {
            // Re-enable merge button so user can try again
            worktree.IsMergePending = false;
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
            // Re-enable merge button so user can try again
            worktree.IsMergePending = false;
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
            // Check if confirmation is enabled in settings
            if (_settingsService.ShowDeleteConfirmation)
            {
                var confirmed = await _dialogService.ShowConfirmAsync("Delete Worktree",
                    $"Are you sure you want to delete worktree '{worktree.BranchName}'?\n\nThis action cannot be undone.");

                if (!confirmed) return;
            }

            // Close any open session documents for this worktree
            Factory?.RemoveSessionDocumentsByWorktree(worktree.Id);

            // Close any open file documents from this worktree's path
            Factory?.RemoveFileDocumentsByWorktreePath(worktree.Path);

            await _worktreeService.DeleteWorktreeAsync(
                CurrentRepositoryPath,
                worktree.Id,
                force: true);

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

    private async Task OnRunRequestedAsync(WorktreeViewModel worktree)
    {
        if (!_repositorySettingsService.HasExecutable) return;

        try
        {
            var success = await _repositorySettingsService.RunExecutableAsync(worktree.Path);
            if (!success)
            {
                await _dialogService.ShowErrorAsync("Run Failed",
                    "Failed to run the configured executable. Check the executable path in Repository Settings.");
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Run Failed",
                $"Failed to run executable: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs the configured executable for a worktree by its ID. Called from SessionDocumentViewModel.
    /// </summary>
    public async Task RunExecutableByWorktreeIdAsync(string worktreeId)
    {
        var worktree = Worktrees.FirstOrDefault(w => w.Id == worktreeId);
        if (worktree != null)
        {
            await OnRunRequestedAsync(worktree);
        }
    }

    private void OnRepositorySettingsChanged(object? sender, EventArgs e)
    {
        _dispatcher.Post(() =>
        {
            // Update CanRun on all worktrees
            var canRun = _repositorySettingsService.HasExecutable;
            foreach (var worktree in Worktrees)
            {
                worktree.CanRun = canRun;
            }

            // Update CanRun on all open session documents
            UpdateSessionDocumentsCanRun(canRun);

            // Update CanRunMainRepo for the top bar button
            OnPropertyChanged(nameof(CanRunMainRepo));
        });
    }

    private void UpdateSessionDocumentsCanRun(bool canRun)
    {
        // Update all session documents' CanRun property using the factory method
        // This is called when repository settings change
        Factory?.UpdateSessionDocumentsMergeState(Worktrees);
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
                worktreeVm.IsProcessing = true;
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

            // Find the worktree view model
            var worktree = Worktrees.FirstOrDefault(w => w.Id == e.Session.WorktreeId);
            if (worktree != null)
            {
                // Restore accumulated duration from previous sessions if available
                if (_pendingAccumulatedDurations.TryRemove(e.Session.WorktreeId, out var accumulatedDuration))
                {
                    worktree.AccumulatedDuration = accumulatedDuration;
                    worktree.SessionDuration = accumulatedDuration;
                }

                // Only start turn timer if session is actively processing (not idle/waiting for input)
                if (e.Session.State is Core.Models.SessionState.Processing
                    or Core.Models.SessionState.Active
                    or Core.Models.SessionState.Starting)
                {
                    worktree.StartTurn();
                }
            }
        });
    }

    private void OnSessionEnded(object? sender, SessionEndedEventArgs e)
    {
        _dispatcher.Post(async () =>
        {
            // Update worktree processing state
            var worktree = Worktrees.FirstOrDefault(w =>
                w.ActiveSessionId == e.SessionId);

            if (worktree != null)
            {
                // Always stop processing when turn ends
                worktree.IsProcessing = false;

                // Set error/interrupted flags based on final state
                worktree.HasError = e.FinalState == Core.Models.SessionState.Error;
                worktree.WasInterrupted = e.FinalState == Core.Models.SessionState.Cancelled;

                // End the current turn to accumulate elapsed time
                worktree.EndTurn();

                // Only clear active session for cancelled/error states
                // For completed sessions, keep HasActiveSession true until user closes the document
                if (e.FinalState is Core.Models.SessionState.Cancelled or Core.Models.SessionState.Error)
                {
                    worktree.HasActiveSession = false;
                    worktree.ActiveSessionId = null;
                    // Reset the timer completely for non-normal endings
                    worktree.ResetTimer();
                }

                // Clear the SessionWasActive flag since session ended normally
                try
                {
                    await _worktreeService.UpdateSessionWasActiveAsync(worktree.Path, false);
                }
                catch
                {
                    // Ignore errors - this is not critical
                }

                // Save accumulated duration to metadata for persistence across app restarts
                try
                {
                    var durationMs = (long)worktree.AccumulatedDuration.TotalMilliseconds;
                    await _worktreeService.UpdateAccumulatedDurationAsync(worktree.Path, durationMs);
                }
                catch
                {
                    // Ignore errors - this is not critical
                }
            }

            // Check if this session has a pending merge retry
            // Fire and forget with proper error handling
            _ = SafeRetryMergeAsync(e.SessionId, e.FinalState);
        });
    }

    private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        _dispatcher.Post(async () =>
        {
            // When a session transitions to an active state (Processing, Active, Starting),
            // ensure the worktree shows it as having an active session.
            // This is needed when a completed session is resumed with a follow-up message.
            if (e.Session.State is Core.Models.SessionState.Processing
                or Core.Models.SessionState.Active
                or Core.Models.SessionState.Starting)
            {
                var worktree = Worktrees.FirstOrDefault(w => w.Id == e.Session.WorktreeId);
                if (worktree != null)
                {
                    if (!worktree.HasActiveSession)
                    {
                        worktree.HasActiveSession = true;
                        worktree.ActiveSessionId = e.Session.Id;
                    }
                    worktree.IsProcessing = true;
                    // Clear error/interrupted flags when processing starts
                    worktree.HasError = false;
                    worktree.WasInterrupted = false;

                    // Start turn timer when processing begins (handles resumed sessions)
                    worktree.StartTurn();
                }

                // Persist that session is active so we can restore on app restart
                if (worktree != null)
                {
                    try
                    {
                        await _worktreeService.UpdateSessionWasActiveAsync(worktree.Path, true);
                    }
                    catch
                    {
                        // Ignore errors - this is not critical
                    }
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

            // Prevent race condition: check if we're already opening a session for this worktree
            // If another call is in progress, just activate the existing document when it's ready
            if (!_worktreesOpeningSession.TryAdd(worktree.Id, 0))
            {
                // Session is already being opened for this worktree - skip duplicate creation
                return;
            }

            try
            {
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
            finally
            {
                // Always release the lock when done
                _worktreesOpeningSession.TryRemove(worktree.Id, out _);
            }
        }
        catch (Exception ex)
        {
            _pendingSessionPreviewStates.TryRemove(worktree.Id, out _);
            _worktreesOpeningSession.TryRemove(worktree.Id, out _);
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
        // Use a timeout to prevent hanging on large session files
        var historyService = new SessionHistoryService();
        IReadOnlyList<SessionHistoryMessage> history;
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            try
            {
                history = await historyService.ReadSessionHistoryAsync(worktree.Path, claudeSessionId, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // History loading timed out - create session without history
                history = Array.Empty<SessionHistoryMessage>();
            }
        }

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

        // Store the accumulated duration to be applied when the session document is created
        if (worktree.AccumulatedDurationMs > 0)
        {
            _pendingAccumulatedDurations[worktree.Id] = TimeSpan.FromMilliseconds(worktree.AccumulatedDurationMs);
        }

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

    private void OnAutoSplitLayoutChanged(object? sender, SplitLayout layout)
    {
        _dispatcher.Post(() => UpdateAutoSplitLayoutText(layout));
    }

    private void OnSessionDocumentClosed(object? sender, Views.Docking.SessionDocumentClosedEventArgs e)
    {
        _dispatcher.Post(async () =>
        {
            // Find the worktree and clear its active session state
            var worktree = Worktrees.FirstOrDefault(w => w.Id == e.WorktreeId);
            if (worktree != null)
            {
                // Save the accumulated duration before clearing state
                try
                {
                    var durationMs = (long)worktree.AccumulatedDuration.TotalMilliseconds;
                    if (durationMs > 0)
                    {
                        await _worktreeService.UpdateAccumulatedDurationAsync(worktree.Path, durationMs);
                    }
                }
                catch
                {
                    // Ignore errors - not critical
                }

                worktree.HasActiveSession = false;
                worktree.ActiveSessionId = null;
                worktree.IsProcessing = false;
                // Don't reset the timer - keep the accumulated duration visible
            }

            // End the session in the session service
            try
            {
                await _sessionService.EndSessionAsync(e.SessionId);
            }
            catch
            {
                // Session may already be ended, ignore errors
            }
        });
    }

    private void UpdateAutoSplitLayoutText(SplitLayout layout)
    {
        AutoSplitLayoutText = layout switch
        {
            SplitLayout.Vertical => "Auto-Split: Vertically",
            SplitLayout.Horizontal => "Auto-Split: Horizontally",
            SplitLayout.Grid => "Auto-Split: Grid",
            _ => null
        };
    }

    /// <summary>
    /// Opens a diff document when clicked in the diff browser.
    /// </summary>
    /// <param name="localPath">The local repository path.</param>
    /// <param name="worktreePath">The worktree path to compare against.</param>
    /// <param name="relativePath">The relative path of the file to diff.</param>
    /// <param name="isPreview">If true, opens as a preview tab (replaced on next single-click).</param>
    public Task OpenDiffDocumentAsync(string localPath, string worktreePath, string relativePath, bool isPreview)
    {
        try
        {
            var document = new DiffDocumentViewModel(localPath, worktreePath, relativePath);
            Factory?.AddDiffDocument(document, isPreview);
        }
        catch (Exception ex)
        {
            _dialogService.ShowErrorAsync("Error Opening Diff",
                $"Failed to open diff: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Loads diff entries between local path and a worktree path.
    /// </summary>
    /// <param name="localPath">The local repository path.</param>
    /// <param name="worktreePath">The worktree path to compare against.</param>
    public async Task<IReadOnlyList<Git.Models.DiffEntry>> LoadDiffEntriesAsync(string localPath, string worktreePath)
    {
        try
        {
            return await _gitService.GetDiffEntriesBetweenPathsAsync(localPath, worktreePath);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Error Loading Diff",
                $"Failed to load diff entries: {ex.Message}");
            return Array.Empty<Git.Models.DiffEntry>();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _sessionService.SessionCreated -= OnSessionCreated;
        _sessionService.SessionEnded -= OnSessionEnded;
        _sessionService.SessionStateChanged -= OnSessionStateChanged;
        _sessionService.ClaudeSessionIdReceived -= OnClaudeSessionIdReceived;

        // Unsubscribe from repository settings events
        _repositorySettingsService.SettingsChanged -= OnRepositorySettingsChanged;

        // Unsubscribe from factory events
        if (_factory != null)
        {
            _factory.AutoSplitLayoutChanged -= OnAutoSplitLayoutChanged;
            _factory.SessionDocumentClosed -= OnSessionDocumentClosed;
        }
    }
}

/// <summary>
/// Represents a recent repository item for display in the File menu.
/// </summary>
public class RecentRepositoryItem
{
    public string Path { get; }
    public string DisplayName { get; }
    public IAsyncRelayCommand<string> OpenCommand { get; }

    public RecentRepositoryItem(string path, IAsyncRelayCommand<string> openCommand)
    {
        Path = path;
        DisplayName = System.IO.Path.GetFileName(path.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar));
        OpenCommand = openCommand;
    }
}
