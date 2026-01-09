# Claude Code Orchestrator

A desktop application that provides an integrated IDE-like interface for working with Claude Code through a graphical user interface. Manage multiple development worktrees, create and manage Claude Code sessions, view diffs, and browse files — all within a unified, customizable docking layout.

## Features

- **Session Management** — Create, manage, and resume Claude Code sessions with persistent history
- **Worktree Management** — Create, delete, and switch between Git worktrees for parallel development
- **File Browser** — Browse and view files across worktrees with syntax highlighting
- **Diff Browser** — Visual diff comparison between local and worktree files
- **Multi-tab Interface** — Docking layout with customizable, draggable windows
- **Auto-split Layout** — Automatic window arrangement for multiple concurrent sessions
- **Settings Panel** — Configurable preferences for confirmations, auto-open behavior, and display options
- **Status Badges** — Visual indicators for worktree status (ahead/behind, modified files)

## Tech Stack

- **Language:** C# / .NET 8.0
- **UI Framework:** [Avalonia](https://avaloniaui.net/) 11.2.6 (cross-platform desktop)
- **Docking:** [Dock.Avalonia](https://github.com/wieslawsoltes/Dock) 11.2.6
- **MVVM:** [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) 8.4.0
- **Git:** [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) 0.30.0
- **Code Editing:** [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) 0.10.12

## Project Structure

```
├── ClaudeCodeOrchestrator.SDK/    # SDK for interacting with Claude Code CLI
├── ClaudeCodeOrchestrator.Core/   # Core business logic (sessions, worktrees, services)
├── ClaudeCodeOrchestrator.Git/    # Git integration using LibGit2Sharp
└── ClaudeCodeOrchestrator.App/    # Avalonia desktop application (UI and ViewModels)
```

## Building

Requires .NET 8.0 SDK.

```bash
dotnet build
```

## Running

```bash
dotnet run --project ClaudeCodeOrchestrator.App
```

Or launch the compiled executable directly from the build output.

## Configuration

Settings are stored in:
- **Windows:** `%APPDATA%/ClaudeCodeOrchestrator/settings.json`
- **macOS:** `~/Library/Application Support/ClaudeCodeOrchestrator/settings.json`
- **Linux:** `~/.config/ClaudeCodeOrchestrator/settings.json`

Available settings include:
- Confirmation dialogs for merge, delete, and close operations
- Auto-open sessions when creating worktrees
- Output panel visibility
- Worktree list display preferences

## License

MIT
