# Claude Code Instructions

## Build Commands

### .NET Build and Restore

When running `dotnet build` or `dotnet restore`, always run them in the background and poll the status:

1. Run the command in background mode using `run_in_background: true`
2. Use `TaskOutput` with `block: false` to poll the status periodically
3. This prevents long builds from blocking the conversation and allows monitoring progress
