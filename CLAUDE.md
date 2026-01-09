# Claude Code Instructions

## Tool Usage Restrictions

### test-app Skill and Chrome DevTools MCP

Do NOT automatically use the `test-app` skill or Chrome DevTools MCP tools. Only use these features when the user explicitly asks for them. These tools should not be invoked proactively.

## Build Commands

### .NET Build and Restore

When running `dotnet build` or `dotnet restore`, always run them in the background and poll the status:

1. Run the command in background mode using `run_in_background: true`
2. Use `TaskOutput` with `block: false` to poll the status periodically
3. This prevents long builds from blocking the conversation and allows monitoring progress
