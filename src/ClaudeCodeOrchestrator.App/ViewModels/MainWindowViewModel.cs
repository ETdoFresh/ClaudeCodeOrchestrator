using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
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
            var taskDescription = await _dialogService.ShowNewTaskDialogAsync();
            if (string.IsNullOrEmpty(taskDescription)) return;

            // Create worktree
            var worktree = await _worktreeService.CreateWorktreeAsync(
                CurrentRepositoryPath,
                taskDescription);

            // Add to list
            var vm = WorktreeViewModel.FromModel(worktree);
            SetupWorktreeCallbacks(vm);
            Worktrees.Insert(0, vm);

            // Sync to dock panel
            Factory?.AddWorktree(vm);

            // Create session for the worktree
            await CreateSessionForWorktreeAsync(worktree, taskDescription);
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

    private async Task RefreshWorktreesAsync()
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
                worktree.Status = WorktreeStatus.Merged;
            }
            else
            {
                var message = result.ConflictingFiles?.Count > 0
                    ? $"Merge conflicts in: {string.Join(", ", result.ConflictingFiles)}"
                    : result.ErrorMessage ?? "Unknown error";

                await _dialogService.ShowErrorAsync("Merge Failed", message);
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Error Merging",
                $"Failed to merge worktree: {ex.Message}");
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

    private async Task CreateSessionForWorktreeAsync(WorktreeInfo worktree, string prompt)
    {
        try
        {
            var session = await _sessionService.CreateSessionAsync(worktree, prompt);

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

            // Add to document dock via factory
            Factory?.AddSessionDocument(document);
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
        });
    }

    private string GetWorktreeBranch(string worktreeId)
    {
        return Worktrees.FirstOrDefault(w => w.Id == worktreeId)?.BranchName ?? "unknown";
    }

    /// <summary>
    /// Opens a session for a worktree when clicked in the worktrees panel.
    /// </summary>
    public async Task OpenWorktreeSessionAsync(WorktreeViewModel worktree)
    {
        if (string.IsNullOrEmpty(CurrentRepositoryPath)) return;

        try
        {
            // Check if session already exists for this worktree
            if (worktree.HasActiveSession && !string.IsNullOrEmpty(worktree.ActiveSessionId))
            {
                // Activate existing session document
                Factory?.ActivateSessionDocument(worktree.ActiveSessionId);
                return;
            }

            // Get the WorktreeInfo to create a session
            var worktreeInfo = await _worktreeService.GetWorktreeAsync(
                CurrentRepositoryPath, worktree.Id);

            if (worktreeInfo is null) return;

            // Create new session with continuation prompt
            await CreateSessionForWorktreeAsync(worktreeInfo, "Continue working on this task.");
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Error Opening Session",
                $"Failed to open session: {ex.Message}");
        }
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
    }
}
