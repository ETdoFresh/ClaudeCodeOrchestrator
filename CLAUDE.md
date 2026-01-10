# Claude Code Instructions

## IMPORTANT: Tool Usage Restrictions

### Build Commands (.NET Build and Restore)

**ALWAYS** run `dotnet build` or `dotnet restore` in the background and poll the status:

1. Run the command in background mode using `run_in_background: true`
2. Use `TaskOutput` with `block: false` to poll the status periodically
3. This prevents long builds from blocking the conversation and allows monitoring progress

### test-app Skill and Chrome DevTools MCP

**Do NOT** automatically use the `test-app` skill or Chrome DevTools MCP tools. Only use these features when the user **explicitly** asks for them. These tools should not be invoked proactively.

---

## Project Structure

This is a .NET 8 Avalonia desktop application for orchestrating Claude Code sessions with git worktree support.

```
ClaudeCodeOrchestrator/
├── CLAUDE.md                          # Claude Code instructions
├── ClaudeCodeOrchestrator.sln         # Visual Studio solution file
├── icons/                             # Application icons
└── src/
    ├── ClaudeCodeOrchestrator.App/    # Main Avalonia desktop application
    │   ├── Automation/                # UI automation support
    │   ├── Converters/                # XAML value converters
    │   ├── Models/                    # App-specific models
    │   ├── Services/                  # App services (dialogs, settings, dispatcher)
    │   ├── Themes/                    # UI themes and styles
    │   ├── ViewModels/                # MVVM ViewModels
    │   │   └── Docking/               # Dock panel ViewModels
    │   └── Views/                     # XAML views
    │       ├── Controls/              # Reusable UI controls
    │       ├── Dialogs/               # Dialog windows
    │       ├── Docking/               # Dock panel views
    │       └── Panels/                # Panel views
    │
    ├── ClaudeCodeOrchestrator.Core/   # Core business logic layer
    │   ├── Models/                    # Core domain models
    │   └── Services/                  # Core services (session management, title generation)
    │
    ├── ClaudeCodeOrchestrator.Git/    # Git operations layer (LibGit2Sharp)
    │   ├── Models/                    # Git-related models
    │   └── Services/                  # Git and worktree services
    │
    └── ClaudeCodeOrchestrator.SDK/    # Claude Code SDK integration
        ├── Messages/                  # SDK message types (streaming events, content blocks)
        ├── Options/                   # SDK configuration options
        └── Streaming/                 # Streaming response handling
```

### Project Dependencies

- **App** → Core (main application depends on core)
- **Core** → SDK, Git (core orchestrates SDK and Git functionality)
- **Git** → (standalone, uses LibGit2Sharp)
- **SDK** → (standalone, handles Claude Code CLI communication)

