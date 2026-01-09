using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using ClaudeCodeOrchestrator.App.ViewModels.Docking;

namespace ClaudeCodeOrchestrator.App.Views.Docking;

/// <summary>
/// Factory for creating the dock layout.
/// </summary>
public class DockFactory : Factory
{
    private readonly object _context;
    private IDocumentDock? _documentDock;
    private IToolDock? _topDock;
    private FileBrowserViewModel? _fileBrowser;
    private WorktreesViewModel? _worktreesViewModel;
    private bool _isAddingDocument;

    public DockFactory(object context)
    {
        _context = context;
    }

    public override IRootDock CreateLayout()
    {
        // Create tool view models
        _worktreesViewModel = new WorktreesViewModel();
        _fileBrowser = new FileBrowserViewModel();

        // Wire up callbacks if context is MainWindowViewModel
        if (_context is ViewModels.MainWindowViewModel mainVm)
        {
            _worktreesViewModel.OnCreateTaskRequested = () => mainVm.CreateTaskCommand.ExecuteAsync(null);
            _worktreesViewModel.OnRefreshRequested = () => mainVm.RefreshWorktreesAsync();
            _worktreesViewModel.OnWorktreeSelected = (worktree, isPreview) => mainVm.OpenWorktreeSessionAsync(worktree, isPreview);
            _fileBrowser.OnFileSelected = (path, isPreview) => mainVm.OpenFileDocumentAsync(path, isPreview);
        }

        // Top tool dock (worktrees as first tab, file browser as second)
        _topDock = new ToolDock
        {
            Id = "TopDock",
            Title = "Explorer",
            ActiveDockable = _worktreesViewModel,
            VisibleDockables = CreateList<IDockable>(_worktreesViewModel, _fileBrowser),
            Alignment = Alignment.Top,
            GripMode = GripMode.Visible
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

        // Root proportional dock (top panel + documents)
        var rootProportional = new ProportionalDock
        {
            Id = "RootProportional",
            Orientation = Orientation.Vertical,
            ActiveDockable = _documentDock,
            VisibleDockables = CreateList<IDockable>(
                _topDock,
                new ProportionalDockSplitter { Id = "TopSplitter" },
                _documentDock
            )
        };

        // Set proportions (top panel takes less height than documents)
        _topDock.Proportion = 0.25;
        _documentDock.Proportion = 0.75;

        // Root dock
        var rootDock = CreateRootDock();
        rootDock.Id = "Root";
        rootDock.Title = "Root";
        rootDock.ActiveDockable = rootProportional;
        rootDock.DefaultDockable = rootProportional;
        rootDock.VisibleDockables = CreateList<IDockable>(rootProportional);

        return rootDock;
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
        }
        finally
        {
            _isAddingDocument = false;
        }
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
            _fileBrowser.LoadDirectory(path);
        }
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
    }

    /// <summary>
    /// Adds a worktree to the panel.
    /// </summary>
    public void AddWorktree(ViewModels.WorktreeViewModel worktree)
    {
        _worktreesViewModel?.Worktrees.Insert(0, worktree);
    }

    /// <summary>
    /// Removes a worktree from the panel.
    /// </summary>
    public void RemoveWorktree(ViewModels.WorktreeViewModel worktree)
    {
        _worktreesViewModel?.Worktrees.Remove(worktree);
    }

    /// <summary>
    /// Gets the worktrees view model for external updates.
    /// </summary>
    public WorktreesViewModel? GetWorktreesViewModel() => _worktreesViewModel;

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
    /// Closes the current preview document if one exists (file or session).
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
        // Switch to Explorer tab in the top panel
        if (_topDock != null && _fileBrowser != null)
        {
            _topDock.ActiveDockable = _fileBrowser;
        }

        _fileBrowser?.SelectFileByPath(filePath, suppressCallback: true);
    }

    /// <summary>
    /// Highlights a worktree in the worktrees list by selecting it and switches to the Worktrees tab.
    /// </summary>
    private void HighlightWorktreeInList(string worktreeId)
    {
        if (_worktreesViewModel is null || string.IsNullOrEmpty(worktreeId)) return;

        // Switch to Worktrees tab in the top panel
        if (_topDock != null)
        {
            _topDock.ActiveDockable = _worktreesViewModel;
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
            ["TopDock"] = () => _context,
            ["DocumentDock"] = () => _context,
            ["RootProportional"] = () => _context,
            ["FileBrowser"] = () => _context,
            ["Worktrees"] = () => _context
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
}
