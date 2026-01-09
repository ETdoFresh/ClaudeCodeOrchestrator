# Claude Code Orchestrator

A desktop application for managing AI-assisted code development using Git worktrees and persistent Claude AI sessions.

## Overview

Claude Code Orchestrator enables developers to create isolated working environments (Git worktrees) for AI-assisted coding tasks. Each worktree maintains its own Claude session with persistent context across multiple messages, allowing for focused, parallel development workflows.

## Features

### Session Management
- Create and resume Claude Code sessions with persistent conversation history
- Manage multiple concurrent sessions (one per worktree)
- Session cost tracking and state management
- Automatic session title generation

### Git Worktree Management
- Create new Git worktrees from any base branch
- Automatic branch naming with timestamps
- Monitor worktree status (Active, HasChanges, ReadyToMerge, Merged, Locked)
- Merge worktrees back to target branches
- Delete worktrees with cleanup

### File and Diff Viewing
- File browser panel to explore worktree contents
- Syntax-highlighted file viewer
- Diff browser to view changes between repository and worktrees
- HTML-rendered diff display

### User Interface
- Tabbed/docked layout system with split views (Vertical, Horizontal, Grid)
- Auto-split feature to automatically arrange tabs for new sessions
- Customizable settings and preferences
- Output panel for logs and diagnostics

## Technology Stack

- **.NET 8.0** with C# 12
- **Avalonia 11.2** - Cross-platform desktop UI framework
- **Dock.Avalonia** - Dockable/tabbed layout system
- **AvaloniaEdit** - Code editor with syntax highlighting
- **CommunityToolkit.Mvvm** - MVVM pattern with source generators
- **LibGit2Sharp** - Git operations
- **Markdig** - Markdown rendering

## Project Structure

```
src/
├── ClaudeCodeOrchestrator.SDK/      # SDK for Claude Agent interaction
├── ClaudeCodeOrchestrator.Git/      # Git and worktree operations
├── ClaudeCodeOrchestrator.Core/     # Core business logic and session management
└── ClaudeCodeOrchestrator.App/      # Desktop application (Avalonia UI)
```

## Building

### Prerequisites
- .NET 8.0 SDK

### Build
```bash
dotnet build
```

### Run
```bash
dotnet run --project src/ClaudeCodeOrchestrator.App
```

## Architecture

The application follows a layered architecture:

1. **Presentation Layer (App)**: Avalonia UI with MVVM pattern
2. **Business Logic Layer (Core)**: Session management and history tracking
3. **Data Access Layer (Git)**: Git operations via LibGit2Sharp
4. **External Integration Layer (SDK)**: Claude Agent communication

### Key Services
- `ISessionService` - Manages Claude Code sessions
- `IWorktreeService` - Manages Git worktrees
- `IGitService` - Low-level Git operations
- `ISettingsService` - Application preferences
- `ITitleGeneratorService` - AI-powered title generation

## License

See LICENSE file for details.
