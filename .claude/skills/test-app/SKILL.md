---
name: test-app
description: Run integration tests on the Claude Code Orchestrator app. Use this skill to interact with the running app - click buttons, type text, take screenshots, and verify UI state. (project)
---

# Test App Skill

This skill allows you to interact with the running Claude Code Orchestrator application for integration testing.

**IMPORTANT:** This is a native desktop Avalonia app, NOT a web app. Do NOT use Chrome DevTools MCP or any browser-based tools. Use the app's built-in CLI automation commands described below.

## Prerequisites

The app must be running. Start it with:
```bash
cd ./src/ClaudeCodeOrchestrator.App && dotnet run
```

Or if already built (Windows):
```bash
./src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App.exe
```

Or on macOS/Linux:
```bash
./src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App
```

## Targeting a Specific Instance (Required)

Since multiple instances of the app can run simultaneously, you **must** specify which instance to interact with using the `--pid` flag.

### Finding the PID

On Windows:
```powershell
Get-Process -Name "ClaudeCodeOrchestrator.App" | Select-Object Id
```

On macOS/Linux:
```bash
pgrep -f "ClaudeCodeOrchestrator.App"
```

### Using the PID

All CLI commands require the `--pid` flag to target a specific instance:
```bash
$APP --pid $APP_PID --ping
$APP --pid $APP_PID --click FileMenu
$APP --pid $APP_PID --screenshot ./test.png
```

## CLI Commands

The app binary supports automation commands when passed CLI arguments.

Set up the APP variable first:
```bash
# Windows
APP="./src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App.exe"

# macOS/Linux
APP="./src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App"
```

### Check if app is running
```bash
$APP --pid $APP_PID --ping
```

### Click an element
```bash
# By automation ID
$APP --pid $APP_PID --click FileMenu

# By coordinates
$APP --pid $APP_PID --click --x 100 --y 50
```

### Type text
```bash
# Into focused element
$APP --pid $APP_PID --type "Hello World"

# Into specific element
$APP --pid $APP_PID --type "Task description" --id TaskDescriptionInput
```

### Press keys
```bash
# Single key
$APP --pid $APP_PID --key Enter

# Key combination
$APP --pid $APP_PID --key Ctrl+S
```

### Take screenshot
```bash
# Save to file
$APP --pid $APP_PID --screenshot ./test-screenshot.png

# Get base64 (for inline viewing)
$APP --pid $APP_PID --screenshot
```

### List elements with automation IDs
```bash
$APP --pid $APP_PID --elements

# Filter by type
$APP --pid $APP_PID --elements Button
```

### Wait
```bash
# Wait for duration
$APP --pid $APP_PID --wait 1000

# Wait for element to appear
$APP --pid $APP_PID --wait --for NewTaskDialog --timeout 5000
```

### Focus element
```bash
$APP --pid $APP_PID --focus TaskDescriptionInput
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
# Set up APP variable (adjust for your OS)
APP="./src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App.exe"

# Find running instance PID
APP_PID=$(pgrep -f "ClaudeCodeOrchestrator.App" | head -1)

# Verify app is running
$APP --pid $APP_PID --ping

# Click File menu
$APP --pid $APP_PID --click FileMenu

# Click Open Repository
$APP --pid $APP_PID --click OpenRepositoryMenuItem

# Take screenshot to verify dialog opened
$APP --pid $APP_PID --screenshot ./open-repo-dialog.png
```

### Test: Create a new task (requires open repository)
```bash
APP="./src/ClaudeCodeOrchestrator.App/bin/Debug/net8.0/ClaudeCodeOrchestrator.App.exe"
APP_PID=$(pgrep -f "ClaudeCodeOrchestrator.App" | head -1)

# Click Task menu
$APP --pid $APP_PID --click TaskMenu

# Click New Task
$APP --pid $APP_PID --click NewTaskMenuItem

# Wait for dialog
$APP --pid $APP_PID --wait --for NewTaskDialog --timeout 3000

# Type task description
$APP --pid $APP_PID --type "Add user authentication" --id TaskDescriptionInput

# Click Create
$APP --pid $APP_PID --click TaskCreateButton
```

## Tips

1. **Always specify `--pid`** to target the correct app instance when multiple are running
2. Always check `--ping` first to ensure the app is running and responsive
3. Use `--screenshot` to capture state for verification
4. Use `--wait --for <id>` when expecting dialogs to appear
5. Menu items need the menu to be open first (click FileMenu, then click OpenRepositoryMenuItem)
6. On Windows, use `.exe` extension for the binary path
7. Use relative paths from the repository root for portability
