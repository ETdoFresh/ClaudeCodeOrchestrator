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
    private FileBrowserViewModel? _fileBrowser;
    private WorktreesViewModel? _worktreesViewModel;
    private OutputViewModel? _outputViewModel;

    public DockFactory(object context)
    {
        _context = context;
    }

    public override IRootDock CreateLayout()
    {
        // Create tool view models
        _fileBrowser = new FileBrowserViewModel();
        _worktreesViewModel = new WorktreesViewModel();
        _outputViewModel = new OutputViewModel();

        // Create welcome document
        var welcomeDoc = new SessionDocumentViewModel
        {
            Id = "Welcome",
            Title = "Welcome"
        };

        // Left tool dock (file browser)
        var leftDock = new ToolDock
        {
            Id = "LeftDock",
            Title = "Explorer",
            ActiveDockable = _fileBrowser,
            VisibleDockables = CreateList<IDockable>(_fileBrowser),
            Alignment = Alignment.Left,
            GripMode = GripMode.Visible
        };

        // Bottom tool dock (worktrees, output)
        var bottomDock = new ToolDock
        {
            Id = "BottomDock",
            Title = "Panel",
            ActiveDockable = _worktreesViewModel,
            VisibleDockables = CreateList<IDockable>(_worktreesViewModel, _outputViewModel),
            Alignment = Alignment.Bottom,
            GripMode = GripMode.Visible
        };

        // Document dock (sessions) - store reference for dynamic document creation
        _documentDock = new DocumentDock
        {
            Id = "DocumentDock",
            Title = "Documents",
            ActiveDockable = welcomeDoc,
            VisibleDockables = CreateList<IDockable>(welcomeDoc),
            CanCreateDocument = false // We handle document creation ourselves
        };

        // Main content proportional dock (documents + bottom panel)
        var mainContent = new ProportionalDock
        {
            Id = "MainContent",
            Orientation = Orientation.Vertical,
            ActiveDockable = _documentDock,
            VisibleDockables = CreateList<IDockable>(
                _documentDock,
                new ProportionalDockSplitter { Id = "MainSplitter" },
                bottomDock
            ),
            Proportion = 0.7
        };

        // Root proportional dock (sidebar + main content)
        var rootProportional = new ProportionalDock
        {
            Id = "RootProportional",
            Orientation = Orientation.Horizontal,
            ActiveDockable = mainContent,
            VisibleDockables = CreateList<IDockable>(
                leftDock,
                new ProportionalDockSplitter { Id = "LeftSplitter" },
                mainContent
            )
        };

        // Set proportions
        leftDock.Proportion = 0.2;
        mainContent.Proportion = 0.8;
        _documentDock.Proportion = 0.7;
        bottomDock.Proportion = 0.3;

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
    public void AddSessionDocument(SessionDocumentViewModel document)
    {
        if (_documentDock is null) return;

        // Register context for new document
        if (ContextLocator is Dictionary<string, Func<object?>> contextDict)
        {
            contextDict[document.Id] = () => _context;
        }

        // Add to visible dockables
        _documentDock.VisibleDockables ??= CreateList<IDockable>();
        _documentDock.VisibleDockables.Add(document);

        // Make it active
        _documentDock.ActiveDockable = document;

        // Initialize the document
        InitDockable(document, _documentDock);
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
            _documentDock.ActiveDockable = doc;
        }
    }

    /// <summary>
    /// Removes a session document from the dock.
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
    /// Gets the output view model for logging.
    /// </summary>
    public OutputViewModel? GetOutputViewModel() => _outputViewModel;

    public override void InitLayout(IDockable layout)
    {
        ContextLocator = new Dictionary<string, Func<object?>>
        {
            ["Root"] = () => _context,
            ["LeftDock"] = () => _context,
            ["BottomDock"] = () => _context,
            ["DocumentDock"] = () => _context,
            ["MainContent"] = () => _context,
            ["RootProportional"] = () => _context,
            ["FileBrowser"] = () => _context,
            ["Worktrees"] = () => _context,
            ["Output"] = () => _context,
            ["Welcome"] = () => _context
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
