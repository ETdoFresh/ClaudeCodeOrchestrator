namespace ClaudeCodeOrchestrator.SDK.Options;

/// <summary>
/// Configuration options for Claude Agent queries.
/// </summary>
public sealed record ClaudeAgentOptions
{
    // Session Management
    /// <summary>
    /// Resume a previous session by session ID.
    /// </summary>
    public string? Resume { get; init; }

    /// <summary>
    /// Continue the most recent session.
    /// </summary>
    public bool Continue { get; init; }

    /// <summary>
    /// Fork from an existing session.
    /// </summary>
    public bool ForkSession { get; init; }

    /// <summary>
    /// Resume session at a specific message UUID.
    /// </summary>
    public string? ResumeSessionAt { get; init; }

    // Working Directory & Environment
    /// <summary>
    /// Working directory for the Claude Code session.
    /// </summary>
    public string? Cwd { get; init; }

    /// <summary>
    /// Additional directories to allow Claude to access.
    /// </summary>
    public IReadOnlyList<string>? AdditionalDirectories { get; init; }

    /// <summary>
    /// Environment variables to set for the Claude Code process.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    // Model Configuration
    /// <summary>
    /// Model to use (e.g., "claude-sonnet-4-20250514").
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Fallback model if primary model is unavailable.
    /// </summary>
    public string? FallbackModel { get; init; }

    /// <summary>
    /// Maximum thinking tokens for extended thinking.
    /// </summary>
    public int? MaxThinkingTokens { get; init; }

    /// <summary>
    /// Maximum number of turns before stopping.
    /// </summary>
    public int? MaxTurns { get; init; }

    /// <summary>
    /// Maximum budget in USD before stopping.
    /// </summary>
    public decimal? MaxBudgetUsd { get; init; }

    // Tools & Permissions
    /// <summary>
    /// List of tools to allow.
    /// </summary>
    public IReadOnlyList<string>? AllowedTools { get; init; }

    /// <summary>
    /// List of tools to disallow.
    /// </summary>
    public IReadOnlyList<string>? DisallowedTools { get; init; }

    /// <summary>
    /// Permission mode for tool usage.
    /// </summary>
    public PermissionMode PermissionMode { get; init; } = PermissionMode.Default;

    /// <summary>
    /// Skip all permission checks (dangerous).
    /// </summary>
    public bool AllowDangerouslySkipPermissions { get; init; }

    // System Prompt
    /// <summary>
    /// System prompt configuration.
    /// </summary>
    public SystemPromptConfig? SystemPrompt { get; init; }

    // Output
    /// <summary>
    /// Include partial streaming messages.
    /// </summary>
    public bool IncludePartialMessages { get; init; }

    // Execution
    /// <summary>
    /// Path to the Claude Code executable.
    /// </summary>
    public string? PathToClaudeCodeExecutable { get; init; }

    /// <summary>
    /// Runtime to use for Claude Code.
    /// </summary>
    public ClaudeRuntime Runtime { get; init; } = ClaudeRuntime.Node;

    /// <summary>
    /// Additional arguments to pass to the executable.
    /// </summary>
    public IReadOnlyList<string>? ExecutableArgs { get; init; }

    // File Checkpointing
    /// <summary>
    /// Enable file checkpointing for rewind support.
    /// </summary>
    public bool EnableFileCheckpointing { get; init; }
}

/// <summary>
/// Permission modes for tool execution.
/// </summary>
public enum PermissionMode
{
    /// <summary>
    /// Default permission behavior.
    /// </summary>
    Default,

    /// <summary>
    /// Automatically accept all tool uses.
    /// </summary>
    AcceptAll,

    /// <summary>
    /// Plan mode - no writes allowed.
    /// </summary>
    Plan
}

/// <summary>
/// Runtime options for Claude Code executable.
/// </summary>
public enum ClaudeRuntime
{
    /// <summary>
    /// Use Node.js runtime.
    /// </summary>
    Node,

    /// <summary>
    /// Use Deno runtime.
    /// </summary>
    Deno
}

/// <summary>
/// System prompt configuration.
/// </summary>
public sealed record SystemPromptConfig
{
    /// <summary>
    /// Custom system prompt text to prepend.
    /// </summary>
    public string? Prepend { get; init; }

    /// <summary>
    /// Custom system prompt text to append.
    /// </summary>
    public string? Append { get; init; }
}
