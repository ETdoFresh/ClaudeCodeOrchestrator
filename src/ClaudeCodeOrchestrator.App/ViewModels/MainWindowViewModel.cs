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

    // Track pending accumulated costs for sessions being restored with history
    private readonly ConcurrentDictionary<string, decimal> _pendingAccumulatedCosts = new();

    // Track worktrees currently having sessions opened (prevents duplicate tabs from race conditions)
    private readonly ConcurrentDictionary<string, byte> _worktreesOpeningSession = new();

    // Track active jobs with their configurations (worktreeId -> ActiveJob)
    private readonly ConcurrentDictionary<string, ActiveJob> _activeJobs = new();

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

    /// <summary>
    /// Gets whether the VS Code button for the main repository should be visible.
    /// True when a repository is open and VS Code is available.
    /// </summary>
    public bool CanOpenMainRepoInVSCode => IsRepositoryOpen && _repositorySettingsService.IsVSCodeAvailable;

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

            // Note: Previously reconnected active sessions here, but this was removed
            // to prevent unexpected tabs opening on startup. Users can manually click
            // on worktrees to open their sessions.
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
                RecentRepositories.Add(new RecentRepositoryItem(path, OpenRecentRepositoryAsync));
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

        // Load saved prompts and wire up save callback
        var savedPrompts = _repositorySettingsService.GetSavedPrompts();
        Factory?.LoadSavedPrompts(savedPrompts);
        Factory?.SetSavePromptsCallback(SaveJobPrompts);
    }

    private void SaveJobPrompts()
    {
        if (Factory is null) return;
        var prompts = Factory.GetSavedPrompts();
        _repositorySettingsService.SetSavedPrompts(prompts);
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

        // Use placeholder title immediately so user can start typing right away
        const string placeholderTitle = "Generating...";

        // Generate a quick local branch name synchronously for immediate worktree creation
        var localGenerated = _titleGeneratorService.GenerateTitleSync(taskInput.Text);
        var branchName = localGenerated.BranchName;

        // Create worktree with the placeholder title immediately
        var worktree = await _worktreeService.CreateWorktreeAsync(
            CurrentRepositoryPath,
            taskInput.Text,
            title: placeholderTitle,
            branchName: branchName,
            baseBranch: null,
            taskBranchPrefix: _repositorySettingsService.TaskBranchPrefix,
            jobBranchPrefix: _repositorySettingsService.JobBranchPrefix);

        // Add to list
        var vm = WorktreeViewModel.FromModel(worktree);
        SetupWorktreeCallbacks(vm);
        Worktrees.Insert(0, vm);

        // Sync to dock panel
        Factory?.AddWorktree(vm);

        // Create session for the worktree with images using placeholder title
        var fullTaskInput = TaskInput.Create(
            taskInput.Text,
            taskInput.Images.ToList(),
            placeholderTitle,
            branchName);
        await CreateSessionForWorktreeAsync(worktree, fullTaskInput);

        // Start async title generation in background
        // When complete, update the session and worktree with the real title
        _ = GenerateTitleInBackgroundAsync(worktree, vm, taskInput.Text);
    }

    /// <summary>
    /// Generates title asynchronously and updates the session/worktree when complete.
    /// </summary>
    private async Task GenerateTitleInBackgroundAsync(
        Git.Models.WorktreeInfo worktree,
        WorktreeViewModel worktreeVm,
        string promptText)
    {
        try
        {
            var generated = await _titleGeneratorService.GenerateTitleAsync(promptText);
            var title = generated.Title;

            // Update worktree metadata on disk
            await _worktreeService.UpdateTitleAsync(worktree.Path, title);

            // Update worktree view model
            _dispatcher.Post(() =>
            {
                worktreeVm.Title = title;
            });

            // Update the session title (if session exists)
            var session = _sessionService.GetSessionByWorktreeId(worktree.Id);
            if (session != null)
            {
                _sessionService.UpdateSessionTitle(session.Id, title);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MainWindowViewModel] Background title generation failed: {ex.Message}");
            // Keep the placeholder title on failure
        }
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

        // Clear pending accumulated costs
        _pendingAccumulatedCosts.Clear();

        // Clear worktrees opening session lock
        _worktreesOpeningSession.Clear();

        // Notify CanRunMainRepo and CanOpenMainRepoInVSCode since IsRepositoryOpen changed
        OnPropertyChanged(nameof(CanRunMainRepo));
        OnPropertyChanged(nameof(CanOpenMainRepoInVSCode));
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
        var result = await _dialogService.ShowRepositorySettingsAsync(
            _repositorySettingsService.Settings?.Executable,
            _repositorySettingsService.TaskBranchPrefix,
            _repositorySettingsService.JobBranchPrefix);

        if (result != null)
        {
            _repositorySettingsService.SetExecutable(result.Executable);
            _repositorySettingsService.SetBranchPrefixes(result.TaskBranchPrefix, result.JobBranchPrefix);
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

    [RelayCommand]
    private async Task OpenMainRepoInVSCodeAsync()
    {
        if (!_repositorySettingsService.IsVSCodeAvailable || string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        try
        {
            var success = await _repositorySettingsService.OpenInVSCodeAsync(CurrentRepositoryPath);
            if (!success)
            {
                await _dialogService.ShowErrorAsync("Open in VS Code Failed",
                    "Failed to open VS Code. Make sure the 'code' command is available in your PATH.");
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Open in VS Code Failed",
                $"Failed to open VS Code: {ex.Message}");
        }
    }

    public void SetRepository(string path)
    {
        CurrentRepositoryPath = path;
        IsRepositoryOpen = true;
        // TrimEnd to handle paths with trailing slashes
        var repoName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        WindowTitle = $"Claude Code Orchestrator - {repoName}";

        // Notify CanRunMainRepo and CanOpenMainRepoInVSCode since IsRepositoryOpen changed
        OnPropertyChanged(nameof(CanRunMainRepo));
        OnPropertyChanged(nameof(CanOpenMainRepoInVSCode));
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
        vm.OnOpenInVSCodeRequested = OnOpenInVSCodeRequestedAsync;
        vm.OnPauseJobRequested = OnPauseJobRequested;
        vm.OnResumeJobRequested = OnResumeJobRequestedAsync;
        vm.CanRun = _repositorySettingsService.HasExecutable;
        vm.CanOpenInVSCode = _repositorySettingsService.IsVSCodeAvailable;
    }

    private void OnPauseJobRequested(WorktreeViewModel worktree)
    {
        PauseJob(worktree.Id);
    }

    private async Task OnResumeJobRequestedAsync(WorktreeViewModel worktree)
    {
        await ResumeJobAsync(worktree.Id);
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

    /// <summary>
    /// Deletes a worktree by its ID. Called from SessionDocumentViewModel.
    /// </summary>
    public async Task DeleteWorktreeByIdAsync(string worktreeId)
    {
        var worktree = Worktrees.FirstOrDefault(w => w.Id == worktreeId);
        if (worktree != null)
        {
            await OnDeleteRequestedAsync(worktree);
        }
    }

    private async Task CompleteMergeAsync(WorktreeViewModel worktree)
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        // End the Claude session FIRST to release file handles on the worktree directory
        if (!string.IsNullOrEmpty(worktree.ActiveSessionId))
        {
            try
            {
                await _sessionService.EndSessionAsync(worktree.ActiveSessionId);
                await Task.Delay(500); // Let process fully terminate
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to end session {worktree.ActiveSessionId}: {ex.Message}");
            }
        }

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

            // End the Claude session FIRST to release file handles on the worktree directory
            // This kills the claude.exe process that has its working directory set to the worktree
            if (!string.IsNullOrEmpty(worktree.ActiveSessionId))
            {
                try
                {
                    await _sessionService.EndSessionAsync(worktree.ActiveSessionId);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to end session {worktree.ActiveSessionId}: {ex.Message}");
                }
            }

            // Close any open session documents for this worktree
            Factory?.RemoveSessionDocumentsByWorktree(worktree.Id);

            // Close any open file documents from this worktree's path
            Factory?.RemoveFileDocumentsByWorktreePath(worktree.Path);

            // Remove from UI IMMEDIATELY - don't wait for deletion to complete
            var worktreePath = worktree.Path;
            var worktreeId = worktree.Id;
            var repoPath = CurrentRepositoryPath;

            Worktrees.Remove(worktree);
            Factory?.RemoveWorktree(worktree);

            // Run deletion in background - fire and forget with best effort
            _ = Task.Run(async () =>
            {
                try
                {
                    // Small delay to let the process fully terminate
                    await Task.Delay(500);
                    await _worktreeService.DeleteWorktreeAsync(repoPath, worktreeId, force: true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Background worktree deletion failed: {ex.Message}");
                }
            });
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

    private async Task OnOpenInVSCodeRequestedAsync(WorktreeViewModel worktree)
    {
        if (!_repositorySettingsService.IsVSCodeAvailable) return;

        try
        {
            var success = await _repositorySettingsService.OpenInVSCodeAsync(worktree.Path);
            if (!success)
            {
                await _dialogService.ShowErrorAsync("Open in VS Code Failed",
                    "Failed to open VS Code. Make sure the 'code' command is available in your PATH.");
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Open in VS Code Failed",
                $"Failed to open VS Code: {ex.Message}");
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

    /// <summary>
    /// Opens VS Code for a worktree by its ID. Called from SessionDocumentViewModel.
    /// </summary>
    public async Task OpenInVSCodeByWorktreeIdAsync(string worktreeId)
    {
        var worktree = Worktrees.FirstOrDefault(w => w.Id == worktreeId);
        if (worktree != null)
        {
            await OnOpenInVSCodeRequestedAsync(worktree);
        }
    }

    /// <summary>
    /// Resyncs session history from disk for a worktree. Called from SessionDocumentViewModel.
    /// </summary>
    public async Task ResyncSessionHistoryByWorktreeIdAsync(string worktreeId)
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        var worktree = Worktrees.FirstOrDefault(w => w.Id == worktreeId);
        if (worktree == null) return;

        try
        {
            // Get the WorktreeInfo
            var worktreeInfo = await _worktreeService.GetWorktreeAsync(
                CurrentRepositoryPath, worktreeId);

            if (worktreeInfo == null) return;

            // Find the Claude session ID from metadata or disk
            var historyService = new SessionHistoryService();
            var claudeSessionId = worktreeInfo.ClaudeSessionId;
            if (string.IsNullOrEmpty(claudeSessionId))
            {
                claudeSessionId = historyService.GetMostRecentSession(worktreeInfo.Path);
            }

            if (string.IsNullOrEmpty(claudeSessionId))
            {
                await _dialogService.ShowErrorAsync("No Session History",
                    "Could not find any session history for this worktree.");
                return;
            }

            // Load the history
            var history = await historyService.ReadSessionHistoryAsync(
                worktreeInfo.Path, claudeSessionId);

            if (history.Count == 0)
            {
                // Debug: show the actual path being used
                var debugPath = historyService.GetDebugProjectDirectory(worktreeInfo.Path);
                var sessionFilePath = System.IO.Path.Combine(debugPath, $"{claudeSessionId}.jsonl");
                var fileExists = System.IO.File.Exists(sessionFilePath);
                var fileSize = fileExists ? new System.IO.FileInfo(sessionFilePath).Length : 0;

                await _dialogService.ShowErrorAsync("Empty Session History",
                    $"Session file found ({claudeSessionId}) but contains no messages.\n\n" +
                    $"Worktree: {worktreeInfo.Path}\n\n" +
                    $"Looking in: {debugPath}\n\n" +
                    $"Session file: {sessionFilePath}\n" +
                    $"File exists: {fileExists}, Size: {fileSize} bytes");
                return;
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
                    var contentBlocks = new List<SDK.Messages.ContentBlock>();

                    if (!string.IsNullOrEmpty(msg.Content))
                    {
                        contentBlocks.Add(new SDK.Messages.TextContentBlock { Text = msg.Content });
                    }

                    foreach (var toolUse in msg.ToolUses)
                    {
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

            // Find the existing session document and update it
            if (string.IsNullOrEmpty(worktree.ActiveSessionId)) return;

            var document = Factory?.GetSessionDocument(worktree.ActiveSessionId);
            if (document != null)
            {
                // Clear existing messages and load new ones
                document.Messages.Clear();

                // Get the session from service to update it
                var session = _sessionService.GetSession(worktree.ActiveSessionId);
                if (session != null)
                {
                    session.Messages.Clear();
                    foreach (var msg in historyMessages)
                    {
                        session.Messages.Add(msg);
                    }
                    session.ClaudeSessionId = claudeSessionId;

                    // Also update the worktree metadata with the Claude session ID
                    await _worktreeService.UpdateClaudeSessionIdAsync(
                        worktreeInfo.Path, claudeSessionId);

                    // Reload messages into the document
                    document.LoadMessagesFromSession(session);
                }
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Error Resyncing History",
                $"Failed to resync session history: {ex.Message}");
        }
    }

    private void OnRepositorySettingsChanged(object? sender, EventArgs e)
    {
        _dispatcher.Post(() =>
        {
            // Update CanRun and CanOpenInVSCode on all worktrees
            var canRun = _repositorySettingsService.HasExecutable;
            var canOpenInVSCode = _repositorySettingsService.IsVSCodeAvailable;
            foreach (var worktree in Worktrees)
            {
                worktree.CanRun = canRun;
                worktree.CanOpenInVSCode = canOpenInVSCode;
            }

            // Update CanRun on all open session documents
            UpdateSessionDocumentsCanRun(canRun);

            // Update CanRunMainRepo and CanOpenMainRepoInVSCode for the top bar buttons
            OnPropertyChanged(nameof(CanRunMainRepo));
            OnPropertyChanged(nameof(CanOpenMainRepoInVSCode));
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
            // Check if this is a job session
            var isJob = _activeJobs.TryGetValue(e.Session.WorktreeId, out var job);

            // Check if there's an existing document for this worktree (e.g., from a previous iteration)
            var existingDocument = Factory?.GetSessionDocumentByWorktreeId(e.Session.WorktreeId);
            if (existingDocument != null && isJob && job != null && job.CurrentIteration > 1)
            {
                // Reuse the existing document - just update its session ID to receive new messages
                existingDocument.SessionId = e.Session.Id;
                existingDocument.CurrentIteration = job.CurrentIteration;

                // Append an iteration indicator at the end (between previous and new messages)
                existingDocument.AppendIterationIndicator(
                    job.CurrentIteration,
                    job.Configuration.MaxIterations);

                // Load the initial user message from the new session
                existingDocument.LoadMessagesFromSession(e.Session);

                // Activate the existing document
                Factory?.ActivateSessionDocument(existingDocument.SessionId);
                return;
            }

            // Create new document for session
            var branch = GetWorktreeBranch(e.Session.WorktreeId);
            var document = new SessionDocumentViewModel(
                e.Session.Id,
                e.Session.Title,
                branch,
                e.Session.WorktreeId);

            // Check if this session is loading history asynchronously
            var isLoadingHistory = _pendingHistoryLoads.ContainsKey(e.Session.WorktreeId);
            document.IsLoadingHistory = isLoadingHistory;

            // Set iteration info for job sessions
            if (isJob && job != null)
            {
                document.CurrentIteration = job.CurrentIteration;
                document.MaxIterations = job.Configuration.MaxIterations;
            }

            // Load any existing messages from the session (for resumed sessions)
            // If loading history async, this will be empty and will be populated later
            if (!isLoadingHistory)
            {
                document.LoadMessagesFromSession(e.Session);

                // Add session started indicator for new sessions (not history loads)
                // This will be inserted at the beginning of the messages
                document.InsertSessionStartedIndicator(
                    isJob,
                    job?.CurrentIteration,
                    job?.Configuration.MaxIterations);
            }

            // Check if this session should be opened as preview
            var isPreview = _pendingSessionPreviewStates.TryRemove(e.Session.WorktreeId, out var preview) && preview;

            // Add to document dock via factory
            Factory?.AddSessionDocument(document, isPreview);

            // Restore accumulated cost from previous sessions if available
            if (_pendingAccumulatedCosts.TryRemove(e.Session.WorktreeId, out var accumulatedCost))
            {
                document.AccumulatedCostUsd = accumulatedCost;
            }

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

                // Save accumulated cost to metadata for persistence across app restarts
                try
                {
                    var document = Factory?.GetSessionDocument(e.SessionId);
                    if (document != null)
                    {
                        await _worktreeService.UpdateAccumulatedCostAsync(worktree.Path, document.AccumulatedCostUsd);
                    }
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

    // Track sessions that are loading history asynchronously
    private readonly ConcurrentDictionary<string, (string WorktreePath, string ClaudeSessionId)> _pendingHistoryLoads = new();

    /// <summary>
    /// Creates an idle session and loads history from an existing Claude session.
    /// The session is created immediately with a loading state, and history is loaded asynchronously.
    /// </summary>
    private async Task CreateIdleSessionWithHistoryAsync(WorktreeInfo worktree, string claudeSessionId)
    {
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

        // Store the accumulated cost to be applied when the session document is created
        if (worktree.AccumulatedCostUsd > 0)
        {
            _pendingAccumulatedCosts[worktree.Id] = worktree.AccumulatedCostUsd;
        }

        // Mark this session as having pending history load
        _pendingHistoryLoads[worktree.Id] = (worktree.Path, claudeSessionId);

        // Create session IMMEDIATELY with empty messages - document will show loading state
        var session = await _sessionService.CreateIdleSessionAsync(worktree, options, null);

        // Store the Claude session ID for resumption
        session.ClaudeSessionId = claudeSessionId;

        // Load history asynchronously in background (don't await)
        _ = LoadHistoryInBackgroundAsync(session.Id, worktree.Id, worktree.Path, claudeSessionId);
    }

    /// <summary>
    /// Loads session history in the background and updates the document when complete.
    /// </summary>
    private async Task LoadHistoryInBackgroundAsync(string sessionId, string worktreeId, string worktreePath, string claudeSessionId)
    {
        try
        {
            var historyService = new SessionHistoryService();
            IReadOnlyList<SessionHistoryMessage> history;

            // Use a longer timeout for background loading
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                try
                {
                    // Load only the last 100 messages initially for faster loading
                    history = await historyService.ReadSessionHistoryAsync(worktreePath, claudeSessionId, cts.Token, maxMessages: 100);
                }
                catch (OperationCanceledException)
                {
                    history = Array.Empty<SessionHistoryMessage>();
                }
            }

            // Convert history to SDK messages
            var historyMessages = ConvertHistoryToSDKMessages(history, claudeSessionId);

            // Update the session and document on the UI thread
            _dispatcher.Post(() =>
            {
                // Get the session
                var session = _sessionService.GetSession(sessionId);
                if (session == null) return;

                // Add messages to session
                foreach (var msg in historyMessages)
                {
                    session.Messages.Add(msg);
                }

                // Find the document and update it
                var document = Factory?.GetSessionDocument(sessionId);
                if (document != null)
                {
                    document.LoadMessagesFromSession(session);
                    document.IsLoadingHistory = false;
                    // Set disk loading context for message virtualization
                    document.SetDiskLoadingContext(worktreePath, claudeSessionId, history.Count);
                }

                // Clean up pending history load tracking
                _pendingHistoryLoads.TryRemove(worktreeId, out _);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading history in background: {ex.Message}");

            // Clear loading state on error
            _dispatcher.Post(() =>
            {
                var document = Factory?.GetSessionDocument(sessionId);
                if (document != null)
                {
                    document.IsLoadingHistory = false;
                }
                _pendingHistoryLoads.TryRemove(worktreeId, out _);
            });
        }
    }

    /// <summary>
    /// Converts session history messages to SDK messages.
    /// </summary>
    private static List<SDK.Messages.ISDKMessage> ConvertHistoryToSDKMessages(
        IReadOnlyList<SessionHistoryMessage> history,
        string claudeSessionId)
    {
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

        return historyMessages;
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

                // Save the accumulated cost before clearing state
                try
                {
                    var document = Factory?.GetSessionDocument(e.SessionId);
                    if (document != null && document.AccumulatedCostUsd > 0)
                    {
                        await _worktreeService.UpdateAccumulatedCostAsync(worktree.Path, document.AccumulatedCostUsd);
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

    /// <summary>
    /// Starts a long-running job for a worktree with the specified configuration.
    /// </summary>
    /// <param name="worktree">The worktree to run the job on.</param>
    /// <param name="config">The job configuration.</param>
    public async Task StartJobAsync(WorktreeViewModel worktree, Docking.JobConfiguration config)
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        try
        {
            // Get the WorktreeInfo
            var worktreeInfo = await _worktreeService.GetWorktreeAsync(
                CurrentRepositoryPath, worktree.Id);

            if (worktreeInfo is null) return;

            // Check if there's already an active job for this worktree
            if (_activeJobs.ContainsKey(worktree.Id))
            {
                await _dialogService.ShowErrorAsync("Job Already Running",
                    "A job is already running for this worktree. Please wait for it to complete or stop it first.");
                return;
            }

            // Generate the initial prompt based on configuration
            var prompt = config.GeneratePrompt(
                worktreeInfo.TaskDescription,
                null // TODO: Get previous conversation summary if available
            );

            // Create and track the active job
            var activeJob = new ActiveJob
            {
                WorktreeId = worktree.Id,
                WorktreePath = worktreeInfo.Path,
                Configuration = config,
                CurrentIteration = 1,
                InitialPrompt = worktreeInfo.TaskDescription
            };
            _activeJobs[worktree.Id] = activeJob;

            // Update worktree with iteration info
            worktree.CurrentIteration = 1;
            worktree.MaxIterations = config.MaxIterations;
            worktree.IsResumedSessionJob = config.SessionOption == Docking.SessionOption.ResumeSession;

            // Persist job metadata for app restart recovery
            await _worktreeService.UpdateJobMetadataAsync(
                worktreeInfo.Path,
                wasJob: true,
                lastIteration: 1,
                maxIterations: config.MaxIterations);

            // Check if we should resume an existing session or start new
            var existingSession = _sessionService.GetSessionByWorktreeId(worktree.Id);

            var isResumedSession = config.SessionOption == Docking.SessionOption.ResumeSession && existingSession != null;

            if (isResumedSession)
            {
                // Add iteration separator for iteration 1 when resuming
                var sessionDoc = Factory?.GetSessionDocument(existingSession!.Id);
                sessionDoc?.AddIterationSeparator(1, config.MaxIterations, isResumedSession: true);

                // Resume existing session with the prompt
                await _sessionService.SendMessageAsync(existingSession!.Id, prompt);
            }
            else
            {
                // Start a new session - iteration separator is added in OnSessionCreated via InsertSessionStartedIndicator
                await CreateSessionForWorktreeAsync(worktreeInfo, prompt, isPreview: false);
            }
        }
        catch (Exception ex)
        {
            // Clean up job tracking on failure
            _activeJobs.TryRemove(worktree.Id, out _);
            ClearWorktreeIterationInfo(worktree);
            await _dialogService.ShowErrorAsync("Job Start Failed",
                $"Failed to start job: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a new job worktree and starts a long-running job on it.
    /// </summary>
    /// <param name="config">The job configuration.</param>
    public async Task CreateNewJobAsync(Docking.JobConfiguration config)
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        try
        {
            // Generate a unique branch name for the job using the configured prefix
            var jobPrefix = _repositorySettingsService.JobBranchPrefix;
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var branchName = $"{jobPrefix}job-{timestamp}";

            // Determine the task description/prompt to use
            var taskDescription = config.PromptOption == Docking.PromptOption.Other
                ? config.CustomPromptText ?? "Long-running job"
                : config.GeneratePrompt(null, null);

            // Create the worktree
            var worktreeInfo = await _worktreeService.CreateWorktreeAsync(
                CurrentRepositoryPath,
                taskDescription,
                title: null,
                branchName: branchName,
                baseBranch: null,
                taskBranchPrefix: _repositorySettingsService.TaskBranchPrefix,
                jobBranchPrefix: jobPrefix);

            // Refresh worktrees to show the new job worktree
            await RefreshWorktreesAsync();

            // Find the new worktree view model
            var worktreeVm = Worktrees.FirstOrDefault(w => w.Id == worktreeInfo.Id);
            if (worktreeVm == null) return;

            // Start the job on the new worktree
            await StartJobAsync(worktreeVm, config);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Create Job Failed",
                $"Failed to create job: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles job iteration when a session completes.
    /// Called from DockFactory when session completes.
    /// </summary>
    public async Task HandleJobIterationAsync(string worktreeId, Core.Models.SessionState finalState)
    {
        if (!_activeJobs.TryGetValue(worktreeId, out var job))
            return; // Not a job, just a regular session

        var config = job.Configuration;
        var worktree = Worktrees.FirstOrDefault(w => w.Id == worktreeId);

        // Check if session failed or errored - mark job as errored but don't remove it
        if (finalState == Core.Models.SessionState.Error || finalState == Core.Models.SessionState.Cancelled)
        {
            job.State = JobState.Error;
            if (worktree != null)
            {
                worktree.HasError = true;
            }
            // Don't remove the job - allow resumption
            return;
        }

        // Check if job was paused - don't continue to next iteration
        if (job.State == JobState.Paused)
        {
            // Job is paused, don't continue
            return;
        }

        // Handle "Commit on End of Turn" if enabled
        if (config.CommitOnEndOfTurn)
        {
            await TryAutoCommitAsync(job.WorktreePath);
        }

        // Check if we've reached max iterations
        if (job.CurrentIteration >= config.MaxIterations)
        {
            _activeJobs.TryRemove(worktreeId, out _);
            ClearWorktreeIterationInfo(worktree);
            // Job completed - no dialog needed, just stop iterating
            return;
        }

        // Continue with next iteration
        job.CurrentIteration++;

        // Update worktree iteration info
        if (worktree != null)
        {
            worktree.CurrentIteration = job.CurrentIteration;
        }

        // Persist job iteration progress
        await _worktreeService.UpdateJobMetadataAsync(
            job.WorktreePath,
            wasJob: true,
            lastIteration: job.CurrentIteration,
            maxIterations: config.MaxIterations);

        // Generate the continuation prompt
        var prompt = config.GeneratePrompt(job.InitialPrompt, null);

        // Small delay to let UI update
        await Task.Delay(500);

        // Check if we should create a new session for each iteration
        if (config.SessionOption == Docking.SessionOption.NewSession)
        {
            // End the current session and create a fresh one
            var existingSession = _sessionService.GetSessionByWorktreeId(worktreeId);
            if (existingSession != null)
            {
                await _sessionService.EndSessionAsync(existingSession.Id);
            }

            // Get worktree info to create new session
            if (!string.IsNullOrEmpty(CurrentRepositoryPath))
            {
                var worktreeInfo = await _worktreeService.GetWorktreeAsync(CurrentRepositoryPath, worktreeId);
                if (worktreeInfo != null)
                {
                    await CreateSessionForWorktreeAsync(worktreeInfo, prompt, isPreview: false);
                    return;
                }
            }

            // If we couldn't create a new session, clean up
            _activeJobs.TryRemove(worktreeId, out _);
            ClearWorktreeIterationInfo(worktree);
            return;
        }

        // Resume existing session (default behavior)
        var session = _sessionService.GetSessionByWorktreeId(worktreeId);
        if (session == null)
        {
            _activeJobs.TryRemove(worktreeId, out _);
            ClearWorktreeIterationInfo(worktree);
            return;
        }

        // Add iteration separator to the session UI
        var isResumedSession = config.SessionOption == Docking.SessionOption.ResumeSession;
        var sessionDoc = Factory?.GetSessionDocument(session.Id);
        sessionDoc?.AddIterationSeparator(job.CurrentIteration, config.MaxIterations, isResumedSession);

        // Send the continuation message (will resume the Claude session)
        await _sessionService.SendMessageAsync(session.Id, prompt);
    }

    private static void ClearWorktreeIterationInfo(WorktreeViewModel? worktree)
    {
        if (worktree == null) return;
        worktree.CurrentIteration = null;
        worktree.MaxIterations = null;
    }

    /// <summary>
    /// Stops an active job for a worktree.
    /// </summary>
    public void StopJob(string worktreeId)
    {
        _activeJobs.TryRemove(worktreeId, out _);
        var worktree = Worktrees.FirstOrDefault(w => w.Id == worktreeId);
        ClearWorktreeIterationInfo(worktree);
    }

    /// <summary>
    /// Pauses an active job for a worktree.
    /// The job will stop after the current turn completes.
    /// </summary>
    public void PauseJob(string worktreeId)
    {
        if (_activeJobs.TryGetValue(worktreeId, out var job))
        {
            job.State = JobState.Paused;
            var worktree = Worktrees.FirstOrDefault(w => w.Id == worktreeId);
            if (worktree != null)
            {
                worktree.IsJobPaused = true;
            }
        }
    }

    /// <summary>
    /// Resumes a paused or errored job for a worktree.
    /// </summary>
    public async Task ResumeJobAsync(string worktreeId)
    {
        if (!_activeJobs.TryGetValue(worktreeId, out var job))
            return;

        if (job.State != JobState.Paused && job.State != JobState.Error)
            return;

        var worktree = Worktrees.FirstOrDefault(w => w.Id == worktreeId);
        if (worktree != null)
        {
            worktree.IsJobPaused = false;
            worktree.HasError = false;
        }

        job.State = JobState.Running;

        // Get the session and send continuation message
        var session = _sessionService.GetSessionByWorktreeId(worktreeId);
        if (session == null) return;

        // Add iteration separator for the next iteration
        job.CurrentIteration++;
        if (worktree != null)
        {
            worktree.CurrentIteration = job.CurrentIteration;
        }

        var isResumedSession = job.Configuration.SessionOption == Docking.SessionOption.ResumeSession;
        var sessionDoc = Factory?.GetSessionDocument(session.Id);
        sessionDoc?.AddIterationSeparator(job.CurrentIteration, job.Configuration.MaxIterations, isResumedSession);

        // Persist job iteration progress
        await _worktreeService.UpdateJobMetadataAsync(
            job.WorktreePath,
            wasJob: true,
            lastIteration: job.CurrentIteration,
            maxIterations: job.Configuration.MaxIterations);

        // Generate and send continuation prompt
        var prompt = job.Configuration.GeneratePrompt(job.InitialPrompt, null);
        await Task.Delay(500); // Small delay to let UI update
        await _sessionService.SendMessageAsync(session.Id, prompt);
    }

    /// <summary>
    /// Gets the state of an active job.
    /// </summary>
    public JobState? GetJobState(string worktreeId)
    {
        return _activeJobs.TryGetValue(worktreeId, out var job) ? job.State : null;
    }

    /// <summary>
    /// Checks if a job is active for a worktree.
    /// </summary>
    public bool IsJobActive(string worktreeId)
    {
        return _activeJobs.ContainsKey(worktreeId);
    }

    /// <summary>
    /// Gets the current iteration for an active job.
    /// </summary>
    public int? GetJobIteration(string worktreeId)
    {
        return _activeJobs.TryGetValue(worktreeId, out var job) ? job.CurrentIteration : null;
    }

    /// <summary>
    /// Gets the max iterations for an active job.
    /// </summary>
    public int? GetJobMaxIterations(string worktreeId)
    {
        return _activeJobs.TryGetValue(worktreeId, out var job) ? job.Configuration.MaxIterations : null;
    }

    private async Task TryAutoCommitAsync(string worktreePath)
    {
        try
        {
            // Check if there are any changes to commit
            var hasChanges = await _gitService.HasUncommittedChangesAsync(worktreePath);
            if (!hasChanges) return;

            // Stage all changes
            await _gitService.StageAllAsync(worktreePath);

            // Commit with auto-generated message
            var commitMessage = $"Auto-commit at end of job iteration - {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            await _gitService.CommitAsync(worktreePath, commitMessage);
        }
        catch
        {
            // Silently ignore commit errors - job should continue
        }
    }
}

/// <summary>
/// State of an active job.
/// </summary>
public enum JobState
{
    /// <summary>Job is actively running iterations.</summary>
    Running,
    /// <summary>Job is paused and waiting to be resumed.</summary>
    Paused,
    /// <summary>Job encountered an error and stopped.</summary>
    Error,
    /// <summary>Job completed all iterations successfully.</summary>
    Completed
}

/// <summary>
/// Tracks an active job and its state.
/// </summary>
public class ActiveJob
{
    public required string WorktreeId { get; init; }
    public required string WorktreePath { get; init; }
    public required Docking.JobConfiguration Configuration { get; init; }
    public int CurrentIteration { get; set; }
    public string? InitialPrompt { get; init; }
    public JobState State { get; set; } = JobState.Running;
}

/// <summary>
/// Represents a recent repository item for display in the File menu.
/// </summary>
public class RecentRepositoryItem
{
    public string Path { get; }
    public string DisplayName { get; }
    public IAsyncRelayCommand OpenCommand { get; }

    public RecentRepositoryItem(string path, Func<string, Task> openAction)
    {
        Path = path;
        DisplayName = System.IO.Path.GetFileName(path.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar));
        // Create a parameterless command that captures the path
        OpenCommand = new AsyncRelayCommand(() => openAction(path));
    }
}
