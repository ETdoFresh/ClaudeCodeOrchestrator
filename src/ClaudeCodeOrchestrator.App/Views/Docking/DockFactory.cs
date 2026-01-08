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

    public DockFactory(object context)
    {
        _context = context;
    }

    public override IRootDock CreateLayout()
    {
        // Create tool view models
        var fileBrowser = new FileBrowserViewModel();
        var worktrees = new WorktreesViewModel();
        var output = new OutputViewModel();

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
            ActiveDockable = fileBrowser,
            VisibleDockables = CreateList<IDockable>(fileBrowser),
            Alignment = Alignment.Left,
            GripMode = GripMode.Visible
        };

        // Bottom tool dock (worktrees, output)
        var bottomDock = new ToolDock
        {
            Id = "BottomDock",
            Title = "Panel",
            ActiveDockable = worktrees,
            VisibleDockables = CreateList<IDockable>(worktrees, output),
            Alignment = Alignment.Bottom,
            GripMode = GripMode.Visible
        };

        // Document dock (sessions)
        var documentDock = new DocumentDock
        {
            Id = "DocumentDock",
            Title = "Documents",
            ActiveDockable = welcomeDoc,
            VisibleDockables = CreateList<IDockable>(welcomeDoc),
            CanCreateDocument = false
        };

        // Main content proportional dock (documents + bottom panel)
        var mainContent = new ProportionalDock
        {
            Id = "MainContent",
            Orientation = Orientation.Vertical,
            ActiveDockable = documentDock,
            VisibleDockables = CreateList<IDockable>(
                documentDock,
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
        documentDock.Proportion = 0.7;
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
