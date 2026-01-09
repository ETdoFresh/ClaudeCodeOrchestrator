using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using ClaudeCodeOrchestrator.App.ViewModels.Docking;
using ClaudeCodeOrchestrator.App.Services;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeCodeOrchestrator.App.Views.Docking;

/// <summary>
/// Layout orientation for splitting tabs.
/// </summary>
public enum SplitLayout
{
    /// <summary>No split layout.</summary>
    None,
    /// <summary>Split tabs vertically (side by side).</summary>
    Vertical,
    /// <summary>Split tabs horizontally (stacked).</summary>
    Horizontal,
    /// <summary>Split tabs in a grid (2x2 or similar).</summary>
    Grid
}

/// <summary>
/// Factory for creating the dock layout.
/// </summary>
public class DockFactory : Factory
{
    private readonly object _context;
    private readonly ISettingsService _settingsService;
    private IDocumentDock? _documentDock;
    private IToolDock? _leftDock;
    private FileBrowserViewModel? _fileBrowser;
    private DiffBrowserViewModel? _diffBrowser;
    private WorktreesViewModel? _worktreesViewModel;
    private SettingsViewModel? _settingsViewModel;
    private bool _isAddingDocument;
    private IRootDock? _rootDock;
    private IProportionalDock? _rootProportional;

    /// <summary>
    /// Gets or sets the auto-split layout mode. When set to a value other than None,
    /// new session documents will automatically trigger a re-split of all tabs.
    /// This is disabled when tabs are manually closed or collapsed.
    /// </summary>
    public SplitLayout AutoSplitLayout
    {
        get => _autoSplitLayout;
        private set
        {
            if (_autoSplitLayout != value)
            {
                _autoSplitLayout = value;
                AutoSplitLayoutChanged?.Invoke(this, value);
            }
        }
    }
    private SplitLayout _autoSplitLayout = SplitLayout.None;

    /// <summary>
    /// Event raised when the auto-split layout mode changes.
    /// </summary>
    public event EventHandler<SplitLayout>? AutoSplitLayoutChanged;

    public DockFactory(object context, ISettingsService settingsService)
    {
        _context = context;
        _settingsService = settingsService;
    }

    public override IRootDock CreateLayout()
    {
        // Create tool view models
        _worktreesViewModel = new WorktreesViewModel();
        _fileBrowser = new FileBrowserViewModel();
        _diffBrowser = new DiffBrowserViewModel();
        _settingsViewModel = new SettingsViewModel(_settingsService);

        // Wire up callbacks if context is MainWindowViewModel
        if (_context is ViewModels.MainWindowViewModel mainVm)
        {
            _worktreesViewModel.OnCreateTaskRequested = () => mainVm.CreateTaskCommand.ExecuteAsync(null);
            _worktreesViewModel.OnRefreshRequested = () => mainVm.RefreshWorktreesAsync();
            _worktreesViewModel.OnPushRequested = () => mainVm.PushAllBranchesCommand.ExecuteAsync(null);
            _worktreesViewModel.OnWorktreeSelected = (worktree, isPreview) => mainVm.OpenWorktreeSessionAsync(worktree, isPreview);
            _fileBrowser.OnFileSelected = (path, isPreview) => mainVm.OpenFileDocumentAsync(path, isPreview);
            _diffBrowser.OnDiffFileSelected = (localPath, worktreePath, relativePath, isPreview) =>
                mainVm.OpenDiffDocumentAsync(localPath, worktreePath, relativePath, isPreview);
            _diffBrowser.OnLoadDiffEntries = (localPath, worktreePath) =>
                mainVm.LoadDiffEntriesAsync(localPath, worktreePath);
        }

        // Left side panel (worktrees as first tab, file browser as second, diff as third, settings as fourth)
        _leftDock = new ToolDock
        {
            Id = "LeftDock",
            Title = "Explorer",
            ActiveDockable = _worktreesViewModel,
            VisibleDockables = CreateList<IDockable>(_worktreesViewModel, _fileBrowser, _diffBrowser, _settingsViewModel),
            Alignment = Alignment.Left,
            GripMode = GripMode.Hidden
        };

        // Document dock (sessions and file contents) - store reference for dynamic document creation
        // Start with no documents - user can open sessions from worktrees panel
        _documentDock = new DocumentDock
        {
            Id = "DocumentDock",
            Title = "Documents",
            IsCollapsable = false, // Keep document area visible even when empty
            VisibleDockables = CreateList<IDockable>(),
            CanCreateDocument = false // We handle document creation ourselves
        };

        // Subscribe to active dockable changes to close preview when user clicks on persistent tabs
        if (_documentDock is System.ComponentModel.INotifyPropertyChanged notifyPropertyChanged)
        {
            notifyPropertyChanged.PropertyChanged += OnDocumentDockPropertyChanged;
        }

        // Root proportional dock (left panel + documents)
        _rootProportional = new ProportionalDock
        {
            Id = "RootProportional",
            Orientation = Orientation.Horizontal,
            ActiveDockable = _documentDock,
            VisibleDockables = CreateList<IDockable>(
                _leftDock,
                new ProportionalDockSplitter { Id = "LeftSplitter" },
                _documentDock
            )
        };

        // Set proportions (left panel takes less width than documents)
        _leftDock.Proportion = 0.25;
        _documentDock.Proportion = 0.75;

        // Root dock
        _rootDock = CreateRootDock();
        _rootDock.Id = "Root";
        _rootDock.Title = "Root";
        _rootDock.ActiveDockable = _rootProportional;
        _rootDock.DefaultDockable = _rootProportional;
        _rootDock.VisibleDockables = CreateList<IDockable>(_rootProportional);

        return _rootDock;
    }

    /// <summary>
    /// Adds a new session document to the document dock.
    /// </summary>
    /// <param name="document">The session document to add.</param>
    /// <param name="isPreview">If true, replaces any existing preview document.</param>
    public void AddSessionDocument(SessionDocumentViewModel document, bool isPreview)
    {
        if (_documentDock is null) return;

        // Wire up the session completed callback to refresh worktrees
        if (_context is ViewModels.MainWindowViewModel mainVm)
        {
            document.OnSessionCompleted = () => mainVm.RefreshWorktreesAsync();
        }

        _isAddingDocument = true;
        try
        {
            // Register context for new document
            if (ContextLocator is Dictionary<string, Func<object?>> contextDict)
            {
                contextDict[document.Id] = () => _context;
            }

            _documentDock.VisibleDockables ??= CreateList<IDockable>();

            // Check if this session is already open (non-preview)
            var existingDoc = _documentDock.VisibleDockables
                .OfType<SessionDocumentViewModel>()
                .FirstOrDefault(d => d.SessionId == document.SessionId && !d.IsPreview);

            if (existingDoc != null)
            {
                // Close any existing preview since we're switching to a persistent tab
                ClosePreviewDocument();

                // Just activate the existing document
                _documentDock.ActiveDockable = existingDoc;
                return;
            }

            if (isPreview)
            {
                // Remove existing preview document if any
                ClosePreviewDocument();
                document.IsPreview = true;
            }
            else
            {
                // Opening a new persistent document - close the preview
                ClosePreviewDocument();
            }

            // Add to visible dockables
            _documentDock.VisibleDockables.Add(document);

            // Make it active
            _documentDock.ActiveDockable = document;

            // Initialize the document
            InitDockable(document, _documentDock);

            // Auto-split if enabled and we have enough documents
            if (AutoSplitLayout != SplitLayout.None)
            {
                // Schedule re-split after the document is added (need to exit this method first)
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ApplyAutoSplit();
                });
            }
        }
        finally
        {
            _isAddingDocument = false;
        }
    }

    /// <summary>
    /// Applies auto-split if enabled and there are enough documents.
    /// </summary>
    private void ApplyAutoSplit()
    {
        if (AutoSplitLayout == SplitLayout.None) return;

        // Count all documents in the layout
        var allDocuments = new List<IDockable>();
        if (_rootProportional != null)
        {
            CollectAllDocuments(_rootProportional, allDocuments);
        }

        // Need at least 2 documents to split
        if (allDocuments.Count < 2) return;

        // Apply the split
        SplitAllDocuments(AutoSplitLayout);
    }

    /// <summary>
    /// Activates an existing session document by session ID.
    /// </summary>
    public void ActivateSessionDocument(string sessionId)
    {
        if (_documentDock?.VisibleDockables is null) return;

        var doc = _documentDock.VisibleDockables
            .OfType<SessionDocumentViewModel>()
            .FirstOrDefault(d => d.SessionId == sessionId);

        if (doc != null)
        {
            // Close preview when switching to a session
            ClosePreviewDocument();
            _documentDock.ActiveDockable = doc;
        }
    }

    /// <summary>
    /// Gets an existing session document by session ID.
    /// </summary>
    public SessionDocumentViewModel? GetSessionDocument(string sessionId)
    {
        if (_documentDock?.VisibleDockables is null) return null;

        return _documentDock.VisibleDockables
            .OfType<SessionDocumentViewModel>()
            .FirstOrDefault(d => d.SessionId == sessionId);
    }

    /// <summary>
    /// Removes a session document from the dock by session ID.
    /// </summary>
    public void RemoveSessionDocument(string sessionId)
    {
        if (_documentDock?.VisibleDockables is null) return;

        var doc = _documentDock.VisibleDockables
            .OfType<SessionDocumentViewModel>()
            .FirstOrDefault(d => d.SessionId == sessionId);

        if (doc != null)
        {
            _documentDock.VisibleDockables.Remove(doc);

            // Dispose the document
            if (doc is IDisposable disposable)
                disposable.Dispose();
        }
    }

    /// <summary>
    /// Removes all session documents associated with a worktree.
    /// </summary>
    public void RemoveSessionDocumentsByWorktree(string worktreeId)
    {
        if (_documentDock?.VisibleDockables is null) return;

        var docsToRemove = _documentDock.VisibleDockables
            .OfType<SessionDocumentViewModel>()
            .Where(d => d.WorktreeId == worktreeId)
            .ToList();

        foreach (var doc in docsToRemove)
        {
            _documentDock.VisibleDockables.Remove(doc);

            // Dispose the document
            if (doc is IDisposable disposable)
                disposable.Dispose();
        }
    }

    /// <summary>
    /// Removes all file documents that are inside a worktree path.
    /// </summary>
    public void RemoveFileDocumentsByWorktreePath(string worktreePath)
    {
        if (_documentDock?.VisibleDockables is null || string.IsNullOrEmpty(worktreePath)) return;

        // Normalize path for comparison
        var normalizedWorktreePath = worktreePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var docsToRemove = _documentDock.VisibleDockables
            .OfType<FileDocumentViewModel>()
            .Where(d => d.FilePath.StartsWith(normalizedWorktreePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                     || d.FilePath.Equals(normalizedWorktreePath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var doc in docsToRemove)
        {
            _documentDock.VisibleDockables.Remove(doc);
        }
    }

    /// <summary>
    /// Updates the file browser with a new root path.
    /// </summary>
    public void UpdateFileBrowser(string? path)
    {
        if (_fileBrowser is null) return;

        if (string.IsNullOrEmpty(path))
        {
            _fileBrowser.ClearDirectory();
        }
        else
        {
            // Update sources with current worktrees list
            var worktrees = _worktreesViewModel?.Worktrees;
            _fileBrowser.UpdateSources(path, worktrees);
        }

        // Also update the diff browser
        UpdateDiffBrowser(path);
    }

    /// <summary>
    /// Updates the diff browser with a new root path.
    /// </summary>
    public void UpdateDiffBrowser(string? path)
    {
        if (_diffBrowser is null) return;

        if (string.IsNullOrEmpty(path))
        {
            _diffBrowser.ClearDiff();
        }
        else
        {
            // Update sources with current worktrees list
            var worktrees = _worktreesViewModel?.Worktrees;
            _diffBrowser.UpdateSources(path, worktrees);
        }
    }

    /// <summary>
    /// Refreshes the file browser sources dropdown with the current worktrees.
    /// Call this after worktrees are updated.
    /// </summary>
    public void RefreshFileBrowserSources()
    {
        if (_fileBrowser?.LocalCopyPath is null) return;

        var worktrees = _worktreesViewModel?.Worktrees;
        _fileBrowser.UpdateSources(_fileBrowser.LocalCopyPath, worktrees);

        // Also refresh diff browser sources
        RefreshDiffBrowserSources();
    }

    /// <summary>
    /// Refreshes the diff browser sources dropdown with the current worktrees.
    /// Call this after worktrees are updated.
    /// </summary>
    public void RefreshDiffBrowserSources()
    {
        if (_diffBrowser?.LocalCopyPath is null) return;

        var worktrees = _worktreesViewModel?.Worktrees;
        _diffBrowser.UpdateSources(_diffBrowser.LocalCopyPath, worktrees);
    }

    /// <summary>
    /// Updates the worktrees panel with a new collection of worktrees.
    /// </summary>
    public void UpdateWorktrees(IEnumerable<ViewModels.WorktreeViewModel> worktrees)
    {
        if (_worktreesViewModel is null) return;

        _worktreesViewModel.Worktrees.Clear();
        foreach (var wt in worktrees)
        {
            _worktreesViewModel.Worktrees.Add(wt);
        }

        // Also refresh the file browser sources dropdown
        RefreshFileBrowserSources();
    }

    /// <summary>
    /// Adds a worktree to the panel.
    /// </summary>
    public void AddWorktree(ViewModels.WorktreeViewModel worktree)
    {
        _worktreesViewModel?.Worktrees.Insert(0, worktree);
        RefreshFileBrowserSources();
    }

    /// <summary>
    /// Removes a worktree from the panel.
    /// </summary>
    public void RemoveWorktree(ViewModels.WorktreeViewModel worktree)
    {
        _worktreesViewModel?.Worktrees.Remove(worktree);
        RefreshFileBrowserSources();
    }

    /// <summary>
    /// Gets the worktrees view model for external updates.
    /// </summary>
    public WorktreesViewModel? GetWorktreesViewModel() => _worktreesViewModel;

    /// <summary>
    /// Adds a diff document to the document dock.
    /// </summary>
    /// <param name="document">The diff document to add.</param>
    /// <param name="isPreview">If true, replaces any existing preview document.</param>
    public void AddDiffDocument(DiffDocumentViewModel document, bool isPreview)
    {
        if (_documentDock is null) return;

        _isAddingDocument = true;
        try
        {
            // Register context for new document
            if (ContextLocator is Dictionary<string, Func<object?>> contextDict)
            {
                contextDict[document.Id] = () => _context;
            }

            _documentDock.VisibleDockables ??= CreateList<IDockable>();

            // Check if this diff is already open (non-preview)
            var existingDoc = _documentDock.VisibleDockables
                .OfType<DiffDocumentViewModel>()
                .FirstOrDefault(d => d.Id == document.Id && !d.IsPreview);

            if (existingDoc != null)
            {
                // Close any existing preview since we're switching to a persistent tab
                ClosePreviewDocument();

                // Just activate the existing document
                _documentDock.ActiveDockable = existingDoc;
                return;
            }

            if (isPreview)
            {
                // Remove existing preview document if any
                ClosePreviewDocument();
                document.IsPreview = true;
            }
            else
            {
                // Opening a new persistent document - close the preview
                ClosePreviewDocument();
            }

            // Add to visible dockables
            _documentDock.VisibleDockables.Add(document);

            // Make it active
            _documentDock.ActiveDockable = document;

            // Initialize the document
            InitDockable(document, _documentDock);
        }
        finally
        {
            _isAddingDocument = false;
        }
    }

    /// <summary>
    /// Adds a file document to the document dock.
    /// </summary>
    /// <param name="document">The file document to add.</param>
    /// <param name="isPreview">If true, replaces any existing preview document.</param>
    public void AddFileDocument(FileDocumentViewModel document, bool isPreview)
    {
        if (_documentDock is null) return;

        _isAddingDocument = true;
        try
        {
            // Register context for new document
            if (ContextLocator is Dictionary<string, Func<object?>> contextDict)
            {
                contextDict[document.Id] = () => _context;
            }

            _documentDock.VisibleDockables ??= CreateList<IDockable>();

            // Check if this file is already open (non-preview)
            var existingDoc = _documentDock.VisibleDockables
                .OfType<FileDocumentViewModel>()
                .FirstOrDefault(d => d.FilePath == document.FilePath && !d.IsPreview);

            if (existingDoc != null)
            {
                // Close any existing preview since we're switching to a persistent tab
                ClosePreviewDocument();

                // Just activate the existing document
                _documentDock.ActiveDockable = existingDoc;
                return;
            }

            if (isPreview)
            {
                // Remove existing preview document if any
                ClosePreviewDocument();
                document.IsPreview = true;
            }
            else
            {
                // Opening a new persistent document - close the preview
                ClosePreviewDocument();
            }

            // Add to visible dockables
            _documentDock.VisibleDockables.Add(document);

            // Make it active
            _documentDock.ActiveDockable = document;

            // Initialize the document
            InitDockable(document, _documentDock);
        }
        finally
        {
            _isAddingDocument = false;
        }
    }

    /// <summary>
    /// Closes the current preview document if one exists (file, session, or diff).
    /// </summary>
    public void ClosePreviewDocument()
    {
        if (_documentDock?.VisibleDockables is null) return;

        // Close file preview
        var existingFilePreview = _documentDock.VisibleDockables
            .OfType<FileDocumentViewModel>()
            .FirstOrDefault(d => d.IsPreview);

        if (existingFilePreview != null)
        {
            _documentDock.VisibleDockables.Remove(existingFilePreview);
        }

        // Close session preview
        var existingSessionPreview = _documentDock.VisibleDockables
            .OfType<SessionDocumentViewModel>()
            .FirstOrDefault(d => d.IsPreview);

        if (existingSessionPreview != null)
        {
            _documentDock.VisibleDockables.Remove(existingSessionPreview);

            // Dispose the session document
            if (existingSessionPreview is IDisposable disposable)
                disposable.Dispose();
        }

        // Close diff preview
        var existingDiffPreview = _documentDock.VisibleDockables
            .OfType<DiffDocumentViewModel>()
            .FirstOrDefault(d => d.IsPreview);

        if (existingDiffPreview != null)
        {
            _documentDock.VisibleDockables.Remove(existingDiffPreview);
        }
    }

    private void OnDocumentDockPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Only handle when user clicks on a tab (not when we're programmatically adding documents)
        if (_isAddingDocument) return;
        if (e.PropertyName != nameof(IDocumentDock.ActiveDockable)) return;
        if (_documentDock?.ActiveDockable is null) return;

        // If user clicked on a persistent file document, close the preview and highlight in explorer
        if (_documentDock.ActiveDockable is FileDocumentViewModel fileDoc && !fileDoc.IsPreview)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(ClosePreviewDocument);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => HighlightFileInExplorer(fileDoc.FilePath));
        }
        // If user clicked on a session document, close the preview and highlight worktree
        else if (_documentDock.ActiveDockable is SessionDocumentViewModel sessionDoc)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(ClosePreviewDocument);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => HighlightWorktreeInList(sessionDoc.WorktreeId));
        }
    }

    /// <summary>
    /// Highlights a file in the file browser by selecting it and switches to the Explorer tab.
    /// </summary>
    private void HighlightFileInExplorer(string filePath)
    {
        // Switch to Explorer tab in the left panel
        if (_leftDock != null && _fileBrowser != null)
        {
            _leftDock.ActiveDockable = _fileBrowser;
        }

        _fileBrowser?.SelectFileByPath(filePath, suppressCallback: true);
    }

    /// <summary>
    /// Highlights a worktree in the worktrees list by selecting it and switches to the Worktrees tab.
    /// </summary>
    private void HighlightWorktreeInList(string worktreeId)
    {
        if (_worktreesViewModel is null || string.IsNullOrEmpty(worktreeId)) return;

        // Switch to Worktrees tab in the left panel
        if (_leftDock != null)
        {
            _leftDock.ActiveDockable = _worktreesViewModel;
        }

        var worktree = _worktreesViewModel.Worktrees.FirstOrDefault(w => w.Id == worktreeId);
        if (worktree != null)
        {
            _worktreesViewModel.SelectedWorktree = worktree;
        }
    }

    /// <summary>
    /// Promotes a file preview document to a persistent document.
    /// </summary>
    public void PromotePreviewDocument(string filePath)
    {
        if (_documentDock?.VisibleDockables is null) return;

        var previewDoc = _documentDock.VisibleDockables
            .OfType<FileDocumentViewModel>()
            .FirstOrDefault(d => d.FilePath == filePath && d.IsPreview);

        if (previewDoc != null)
        {
            previewDoc.IsPreview = false;
        }
    }

    /// <summary>
    /// Promotes a session preview document to a persistent document.
    /// </summary>
    public void PromoteSessionPreviewDocument(string sessionId)
    {
        if (_documentDock?.VisibleDockables is null) return;

        var previewDoc = _documentDock.VisibleDockables
            .OfType<SessionDocumentViewModel>()
            .FirstOrDefault(d => d.SessionId == sessionId && d.IsPreview);

        if (previewDoc != null)
        {
            previewDoc.IsPreview = false;
        }
    }

    public override void InitLayout(IDockable layout)
    {
        ContextLocator = new Dictionary<string, Func<object?>>
        {
            ["Root"] = () => _context,
            ["LeftDock"] = () => _context,
            ["DocumentDock"] = () => _context,
            ["RootProportional"] = () => _context,
            ["FileBrowser"] = () => _context,
            ["DiffBrowser"] = () => _context,
            ["Worktrees"] = () => _context,
            ["Settings"] = () => _context
        };

        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
        };

        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new HostWindow()
        };

        base.InitLayout(layout);
    }

    /// <summary>
    /// Called when a dockable is removed from a dock.
    /// Handles recalculating proportions when a split tab is closed.
    /// </summary>
    public override void OnDockableRemoved(IDockable? dockable)
    {
        base.OnDockableRemoved(dockable);

        // Only handle document removals when we have a split layout
        if (dockable is not DocumentViewModelBase)
            return;

        // Disable auto-split when a tab is closed
        AutoSplitLayout = SplitLayout.None;

        if (!CanCollapseSplitDocuments)
            return;

        // Schedule the cleanup on the UI thread to ensure layout is updated
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            CleanupEmptySplitPanes();
        });
    }

    /// <summary>
    /// Cleans up empty document docks in a split layout and recalculates proportions.
    /// </summary>
    private void CleanupEmptySplitPanes()
    {
        if (_rootProportional?.VisibleDockables is null)
            return;

        // Find the split container
        IProportionalDock? splitContainer = null;
        int splitIndex = -1;
        for (int i = 0; i < _rootProportional.VisibleDockables.Count; i++)
        {
            if (_rootProportional.VisibleDockables[i] is IProportionalDock propDock &&
                propDock.Id != "RootProportional")
            {
                splitContainer = propDock;
                splitIndex = i;
                break;
            }
        }

        if (splitContainer is null)
            return;

        // Clean up empty panes and recalculate proportions
        CleanupEmptyPanesRecursive(splitContainer);

        // Count remaining document docks
        var remainingDocDocks = CountDocumentDocks(splitContainer);

        // If only one document dock remains, collapse back to single pane
        if (remainingDocDocks <= 1)
        {
            CollapseSplitDocuments();
        }
        else
        {
            // Recalculate proportions for remaining panes
            RecalculateProportions(splitContainer);
        }
    }

    /// <summary>
    /// Recursively cleans up empty document docks and their associated splitters.
    /// </summary>
    private void CleanupEmptyPanesRecursive(IDock dock)
    {
        if (dock.VisibleDockables is null)
            return;

        // First, recurse into child proportional docks
        foreach (var child in dock.VisibleDockables.ToList())
        {
            if (child is IProportionalDock childProp)
            {
                CleanupEmptyPanesRecursive(childProp);
            }
        }

        // Find and remove empty document docks and their adjacent splitters
        var toRemove = new List<IDockable>();
        for (int i = 0; i < dock.VisibleDockables.Count; i++)
        {
            var child = dock.VisibleDockables[i];

            // Check for empty document dock
            if (child is IDocumentDock docDock &&
                (docDock.VisibleDockables is null || docDock.VisibleDockables.Count == 0))
            {
                toRemove.Add(child);

                // Also mark adjacent splitter for removal
                // Prefer removing the splitter before the empty dock
                if (i > 0 && dock.VisibleDockables[i - 1] is ProportionalDockSplitter prevSplitter)
                {
                    if (!toRemove.Contains(prevSplitter))
                        toRemove.Add(prevSplitter);
                }
                // Otherwise remove the splitter after
                else if (i < dock.VisibleDockables.Count - 1 &&
                         dock.VisibleDockables[i + 1] is ProportionalDockSplitter nextSplitter)
                {
                    if (!toRemove.Contains(nextSplitter))
                        toRemove.Add(nextSplitter);
                }
            }

            // Check for empty nested proportional dock (all children removed)
            if (child is IProportionalDock nestedProp &&
                (nestedProp.VisibleDockables is null ||
                 nestedProp.VisibleDockables.All(d => d is ProportionalDockSplitter)))
            {
                toRemove.Add(child);

                // Also mark adjacent splitter for removal
                if (i > 0 && dock.VisibleDockables[i - 1] is ProportionalDockSplitter prevSplitter)
                {
                    if (!toRemove.Contains(prevSplitter))
                        toRemove.Add(prevSplitter);
                }
                else if (i < dock.VisibleDockables.Count - 1 &&
                         dock.VisibleDockables[i + 1] is ProportionalDockSplitter nextSplitter)
                {
                    if (!toRemove.Contains(nextSplitter))
                        toRemove.Add(nextSplitter);
                }
            }
        }

        // Remove marked items
        foreach (var item in toRemove)
        {
            dock.VisibleDockables.Remove(item);
        }
    }

    /// <summary>
    /// Counts the number of document docks in a dock hierarchy.
    /// </summary>
    private int CountDocumentDocks(IDock dock)
    {
        if (dock.VisibleDockables is null)
            return 0;

        int count = 0;
        foreach (var child in dock.VisibleDockables)
        {
            if (child is IDocumentDock docDock &&
                docDock.VisibleDockables != null &&
                docDock.VisibleDockables.Count > 0)
            {
                count++;
            }
            else if (child is IDock childDock)
            {
                count += CountDocumentDocks(childDock);
            }
        }
        return count;
    }

    /// <summary>
    /// Recalculates proportions for all document docks in a split layout.
    /// </summary>
    private void RecalculateProportions(IProportionalDock splitContainer)
    {
        if (splitContainer.VisibleDockables is null)
            return;

        // Get non-splitter children
        var children = splitContainer.VisibleDockables
            .Where(d => d is not ProportionalDockSplitter)
            .ToList();

        if (children.Count == 0)
            return;

        var newProportion = 1.0 / children.Count;

        foreach (var child in children)
        {
            if (child is IDocumentDock docDock)
            {
                docDock.Proportion = newProportion;
            }
            else if (child is IProportionalDock nestedProp)
            {
                nestedProp.Proportion = newProportion;
                // Recursively recalculate nested proportional docks
                RecalculateProportions(nestedProp);
            }
        }
    }

    /// <summary>
    /// Splits all open document tabs into separate panes.
    /// </summary>
    /// <param name="layout">The layout orientation for splitting.</param>
    public void SplitAllDocuments(SplitLayout layout)
    {
        if (_rootProportional?.VisibleDockables is null)
            return;

        // If already split, collapse first then re-split
        if (CanCollapseSplitDocuments)
        {
            CollapseSplitDocuments();
        }

        if (_documentDock?.VisibleDockables is null)
            return;

        var documents = _documentDock.VisibleDockables.ToList();

        // Need at least 2 documents to split
        if (documents.Count < 2)
            return;

        // Remove the old document dock from the root proportional
        var dockIndex = _rootProportional.VisibleDockables.IndexOf(_documentDock);
        if (dockIndex < 0)
            return;

        // Create new document docks based on layout
        var newDocumentDocks = CreateSplitDocumentDocks(documents, layout);
        if (newDocumentDocks is null)
            return;

        // Replace the single document dock with the new split layout
        _rootProportional.VisibleDockables.RemoveAt(dockIndex);
        _rootProportional.VisibleDockables.Insert(dockIndex, newDocumentDocks);

        // Update the primary document dock reference to the first one for future tab additions
        _documentDock = GetFirstDocumentDock(newDocumentDocks);

        // Initialize the new layout
        InitDockable(newDocumentDocks, _rootProportional);
    }

    /// <summary>
    /// Collapses all split document panes back into a single pane.
    /// Also disables auto-split mode.
    /// </summary>
    public void CollapseSplitDocuments()
    {
        // Disable auto-split when collapsing
        AutoSplitLayout = SplitLayout.None;

        if (_rootProportional?.VisibleDockables is null)
            return;

        // Find all document docks in the layout
        var allDocuments = new List<IDockable>();
        CollectAllDocuments(_rootProportional, allDocuments);

        if (allDocuments.Count == 0)
            return;

        // Find the proportional dock that contains the split layout (not the main document dock)
        IProportionalDock? splitContainer = null;
        int splitIndex = -1;
        for (int i = 0; i < _rootProportional.VisibleDockables.Count; i++)
        {
            if (_rootProportional.VisibleDockables[i] is IProportionalDock propDock &&
                propDock.Id != "RootProportional")
            {
                splitContainer = propDock;
                splitIndex = i;
                break;
            }
        }

        if (splitContainer is null || splitIndex < 0)
            return;

        // Create a new single document dock with all documents
        var newDocumentDock = new DocumentDock
        {
            Id = "DocumentDock",
            Title = "Documents",
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>(),
            CanCreateDocument = false,
            Proportion = 0.75
        };

        foreach (var doc in allDocuments)
        {
            newDocumentDock.VisibleDockables.Add(doc);
        }

        if (allDocuments.Count > 0)
        {
            newDocumentDock.ActiveDockable = allDocuments[0];
        }

        // Replace the split container with the single document dock
        _rootProportional.VisibleDockables.RemoveAt(splitIndex);
        _rootProportional.VisibleDockables.Insert(splitIndex, newDocumentDock);

        // Update reference
        _documentDock = newDocumentDock;

        // Subscribe to property changes for preview behavior
        if (_documentDock is System.ComponentModel.INotifyPropertyChanged notifyPropertyChanged)
        {
            notifyPropertyChanged.PropertyChanged += OnDocumentDockPropertyChanged;
        }

        InitDockable(newDocumentDock, _rootProportional);
    }

    /// <summary>
    /// Enables auto-split mode with the specified layout.
    /// New session documents will automatically trigger a re-split of all tabs.
    /// </summary>
    /// <param name="layout">The layout to use for auto-split.</param>
    public void EnableAutoSplit(SplitLayout layout)
    {
        if (layout == SplitLayout.None)
        {
            DisableAutoSplit();
            return;
        }

        AutoSplitLayout = layout;

        // Apply the split immediately if we have documents
        SplitAllDocuments(layout);
    }

    /// <summary>
    /// Disables auto-split mode and collapses any existing split layout.
    /// </summary>
    public void DisableAutoSplit()
    {
        AutoSplitLayout = SplitLayout.None;
        CollapseSplitDocuments();
    }

    private IProportionalDock? CreateSplitDocumentDocks(List<IDockable> documents, SplitLayout layout)
    {
        return layout switch
        {
            SplitLayout.Vertical => CreateVerticalSplit(documents),
            SplitLayout.Horizontal => CreateHorizontalSplit(documents),
            SplitLayout.Grid => CreateGridSplit(documents),
            _ => null
        };
    }

    private IProportionalDock CreateVerticalSplit(List<IDockable> documents)
    {
        var docks = new List<IDockable>();
        var proportion = 1.0 / documents.Count;
        var counter = 0;

        foreach (var doc in documents)
        {
            if (docks.Count > 0)
            {
                docks.Add(new ProportionalDockSplitter { Id = $"VSplitter_{counter}" });
            }

            var docDock = new DocumentDock
            {
                Id = $"DocumentDock_{counter}",
                Title = "Documents",
                IsCollapsable = false,
                VisibleDockables = CreateList<IDockable>(doc),
                ActiveDockable = doc,
                CanCreateDocument = false,
                Proportion = proportion
            };
            docks.Add(docDock);
            counter++;
        }

        return new ProportionalDock
        {
            Id = "DocumentSplitVertical",
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList(docks.ToArray()),
            Proportion = 0.75
        };
    }

    private IProportionalDock CreateHorizontalSplit(List<IDockable> documents)
    {
        var docks = new List<IDockable>();
        var proportion = 1.0 / documents.Count;
        var counter = 0;

        foreach (var doc in documents)
        {
            if (docks.Count > 0)
            {
                docks.Add(new ProportionalDockSplitter { Id = $"HSplitter_{counter}" });
            }

            var docDock = new DocumentDock
            {
                Id = $"DocumentDock_{counter}",
                Title = "Documents",
                IsCollapsable = false,
                VisibleDockables = CreateList<IDockable>(doc),
                ActiveDockable = doc,
                CanCreateDocument = false,
                Proportion = proportion
            };
            docks.Add(docDock);
            counter++;
        }

        return new ProportionalDock
        {
            Id = "DocumentSplitHorizontal",
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList(docks.ToArray()),
            Proportion = 0.75
        };
    }

    private IProportionalDock CreateGridSplit(List<IDockable> documents)
    {
        // Calculate grid dimensions (aim for roughly square)
        var count = documents.Count;
        var cols = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (int)Math.Ceiling((double)count / cols);

        var rowDocks = new List<IDockable>();
        var docIndex = 0;
        var rowProportion = 1.0 / rows;

        for (int row = 0; row < rows && docIndex < count; row++)
        {
            if (rowDocks.Count > 0)
            {
                rowDocks.Add(new ProportionalDockSplitter { Id = $"RowSplitter_{row}" });
            }

            var colDocks = new List<IDockable>();
            var colProportion = 1.0 / cols;

            for (int col = 0; col < cols && docIndex < count; col++)
            {
                if (colDocks.Count > 0)
                {
                    colDocks.Add(new ProportionalDockSplitter { Id = $"ColSplitter_{row}_{col}" });
                }

                var doc = documents[docIndex++];
                var docDock = new DocumentDock
                {
                    Id = $"DocumentDock_{row}_{col}",
                    Title = "Documents",
                    IsCollapsable = false,
                    VisibleDockables = CreateList<IDockable>(doc),
                    ActiveDockable = doc,
                    CanCreateDocument = false,
                    Proportion = colProportion
                };
                colDocks.Add(docDock);
            }

            var rowDock = new ProportionalDock
            {
                Id = $"DocRow_{row}",
                Orientation = Orientation.Horizontal,
                VisibleDockables = CreateList(colDocks.ToArray()),
                Proportion = rowProportion
            };
            rowDocks.Add(rowDock);
        }

        return new ProportionalDock
        {
            Id = "DocumentSplitGrid",
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList(rowDocks.ToArray()),
            Proportion = 0.75
        };
    }

    private void CollectAllDocuments(IDockable dockable, List<IDockable> documents)
    {
        if (dockable is IDocumentDock docDock && docDock.VisibleDockables != null)
        {
            foreach (var doc in docDock.VisibleDockables)
            {
                if (doc is DocumentViewModelBase)
                {
                    documents.Add(doc);
                }
            }
        }
        else if (dockable is IDock dock && dock.VisibleDockables != null)
        {
            foreach (var child in dock.VisibleDockables)
            {
                CollectAllDocuments(child, documents);
            }
        }
    }

    private IDocumentDock? GetFirstDocumentDock(IDockable dockable)
    {
        if (dockable is IDocumentDock docDock)
        {
            return docDock;
        }

        if (dockable is IDock dock && dock.VisibleDockables != null)
        {
            foreach (var child in dock.VisibleDockables)
            {
                var result = GetFirstDocumentDock(child);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets whether there are enough documents to split.
    /// </summary>
    public bool CanSplitDocuments => (_documentDock?.VisibleDockables?.Count ?? 0) >= 2;

    /// <summary>
    /// Gets whether documents are currently split and can be collapsed.
    /// </summary>
    public bool CanCollapseSplitDocuments
    {
        get
        {
            if (_rootProportional?.VisibleDockables is null)
                return false;

            // Check if there's a split container (a proportional dock other than root)
            return _rootProportional.VisibleDockables
                .Any(d => d is IProportionalDock && d.Id != "RootProportional");
        }
    }

    /// <summary>
    /// Command to split all documents vertically.
    /// </summary>
    public RelayCommand SplitVerticalCommand => new(() => SplitAllDocuments(SplitLayout.Vertical));

    /// <summary>
    /// Command to split all documents horizontally.
    /// </summary>
    public RelayCommand SplitHorizontalCommand => new(() => SplitAllDocuments(SplitLayout.Horizontal));

    /// <summary>
    /// Command to split all documents in a grid.
    /// </summary>
    public RelayCommand SplitGridCommand => new(() => SplitAllDocuments(SplitLayout.Grid));

    /// <summary>
    /// Command to collapse split documents back to a single pane.
    /// </summary>
    public RelayCommand CollapseSplitCommand => new(CollapseSplitDocuments);

    /// <summary>
    /// Resets the view to default state: collapses split tabs and resets left panel width to 25%.
    /// </summary>
    public void ResetToDefaultView()
    {
        // Collapse any split document panes
        if (CanCollapseSplitDocuments)
        {
            CollapseSplitDocuments();
        }

        // Reset left panel proportion to default (25%)
        if (_leftDock != null)
        {
            _leftDock.Proportion = 0.25;
        }

        // Reset document dock proportion to default (75%)
        if (_documentDock != null)
        {
            _documentDock.Proportion = 0.75;
        }
    }
}
