---
name: test-app
description: Run integration tests on the Claude Code Orchestrator app. Use this skill to interact with the running app - click buttons, type text, take screenshots, and verify UI state. (project)
---

# Test App Skill

This skill allows you to interact with the running Claude Code Orchestrator application for integration testing.

## Prerequisites

The app must be running. Start it with:
```bash
cd /Users/etgarcia/code/claude-code-orchestrator/src/ClaudeCodeOrchestrator.App
dotnet run &
```

Or if already built:
```bash
/Users/etgarcia/code/claude-code-orchestrator/src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App &
```

## CLI Commands

The app binary supports automation commands when passed CLI arguments:

### Check if app is running
```bash
/Users/etgarcia/code/claude-code-orchestrator/src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App --ping
```

### Click an element
```bash
# By automation ID
/Users/etgarcia/code/claude-code-orchestrator/src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App --click FileMenu

# By coordinates
/Users/etgarcia/code/claude-code-orchestrator/src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App --click --x 100 --y 50
```

### Type text
```bash
# Into focused element
/Users/etgarcia/code/claude-code-orchestrator/src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App --type "Hello World"

# Into specific element
/Users/etgarcia/code/claude-code-orchestrator/src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App --type "Task description" --id TaskDescriptionInput
```

### Press keys
```bash
# Single key
/Users/etgarcia/code/claude-code-orchestrator/src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App --key Enter

# Key combination
/Users/etgarcia/code/claude-code-orchestrator/src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App --key Ctrl+S
```

### Take screenshot
```bash
# Save to file
/Users/etgarcia/code/claude-code-orchestrator/src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App --screenshot /tmp/test-screenshot.png

# Get base64 (for inline viewing)
/Users/etgarcia/code/claude-code-orchestrator/src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App --screenshot
```

### List elements with automation IDs
```bash
/Users/etgarcia/code/claude-code-orchestrator/src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App --elements

# Filter by type
/Users/etgarcia/code/claude-code-orchestrator/src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App --elements Button
```

### Wait
```bash
# Wait for duration
/Users/etgarcia/code/claude-code-orchestrator/src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App --wait 1000

# Wait for element to appear
/Users/etgarcia/code/claude-code-orchestrator/src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App --wait --for NewTaskDialog --timeout 5000
```

### Focus element
```bash
/Users/etgarcia/code/claude-code-orchestrator/src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App --focus TaskDescriptionInput
```

## Available Automation IDs

### Main Window
- `MainMenu` - The menu bar
- `FileMenu` - File menu
- `OpenRepositoryMenuItem` - File > Open Repository
- `CloseRepositoryMenuItem` - File > Close Repository
- `ExitMenuItem` - File > Exit
- `TaskMenu` - Task menu
- `NewTaskMenuItem` - Task > New Task
- `ViewMenu` - View menu
- `HelpMenu` - Help menu

### Worktrees Panel
- `WorktreesPanel` - The worktrees panel
- `NewTaskButton` - "+ New Task" button
- `RefreshWorktreesButton` - Refresh button

### New Task Dialog
- `NewTaskDialog` - The dialog window
- `TaskDescriptionInput` - Task description text box
- `TaskCreateButton` - Create button
- `TaskCancelButton` - Cancel button
- `TaskErrorText` - Error message text

## Example Test Workflows

### Test: Open a repository
```bash
APP=/Users/etgarcia/code/claude-code-orchestrator/src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App

# Verify app is running
$APP --ping

# Click File menu
$APP --click FileMenu

# Click Open Repository
$APP --click OpenRepositoryMenuItem

# Take screenshot to verify dialog opened
$APP --screenshot /tmp/open-repo-dialog.png
```

### Test: Create a new task (requires open repository)
```bash
APP=/Users/etgarcia/code/claude-code-orchestrator/src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App

# Click Task menu
$APP --click TaskMenu

# Click New Task
$APP --click NewTaskMenuItem

# Wait for dialog
$APP --wait --for NewTaskDialog --timeout 3000

# Type task description
$APP --type "Add user authentication" --id TaskDescriptionInput

# Click Create
$APP --click TaskCreateButton
```

## Tips

1. Always check `--ping` first to ensure the app is running
2. Use `--screenshot` to capture state for verification
3. Use `--wait --for <id>` when expecting dialogs to appear
4. Menu items need the menu to be open first (click FileMenu, then click OpenRepositoryMenuItem)
5. The binary path can be shortened with an alias: `APP=/Users/etgarcia/code/claude-code-orchestrator/src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App`
