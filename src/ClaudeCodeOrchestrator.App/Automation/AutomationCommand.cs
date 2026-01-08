using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeCodeOrchestrator.App.Automation;

/// <summary>
/// Base class for automation commands sent via IPC.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ClickCommand), "click")]
[JsonDerivedType(typeof(TypeTextCommand), "type")]
[JsonDerivedType(typeof(PressKeyCommand), "key")]
[JsonDerivedType(typeof(ScreenshotCommand), "screenshot")]
[JsonDerivedType(typeof(GetElementsCommand), "elements")]
[JsonDerivedType(typeof(WaitCommand), "wait")]
[JsonDerivedType(typeof(FocusCommand), "focus")]
public abstract class AutomationCommand
{
    public static AutomationCommand? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AutomationCommand>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize<AutomationCommand>(this, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

/// <summary>
/// Click on an element by automation ID or coordinates.
/// </summary>
public class ClickCommand : AutomationCommand
{
    /// <summary>
    /// Automation ID of the element to click (e.g., "OpenRepositoryButton").
    /// </summary>
    public string? AutomationId { get; set; }

    /// <summary>
    /// X coordinate for absolute click (if AutomationId not set).
    /// </summary>
    public int? X { get; set; }

    /// <summary>
    /// Y coordinate for absolute click (if AutomationId not set).
    /// </summary>
    public int? Y { get; set; }

    /// <summary>
    /// Whether to double-click.
    /// </summary>
    public bool DoubleClick { get; set; }
}

/// <summary>
/// Type text into the focused element.
/// </summary>
public class TypeTextCommand : AutomationCommand
{
    /// <summary>
    /// Text to type.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Optional automation ID of element to focus first.
    /// </summary>
    public string? AutomationId { get; set; }
}

/// <summary>
/// Press a key or key combination.
/// </summary>
public class PressKeyCommand : AutomationCommand
{
    /// <summary>
    /// Key to press (e.g., "Enter", "Escape", "Tab").
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Modifier keys (e.g., "Ctrl", "Alt", "Shift", "Ctrl+Shift").
    /// </summary>
    public string? Modifiers { get; set; }
}

/// <summary>
/// Take a screenshot of the application window.
/// </summary>
public class ScreenshotCommand : AutomationCommand
{
    /// <summary>
    /// Path to save the screenshot. If not provided, returns base64.
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// Optional automation ID to screenshot specific element.
    /// </summary>
    public string? AutomationId { get; set; }
}

/// <summary>
/// Get a list of all automation elements in the current window.
/// </summary>
public class GetElementsCommand : AutomationCommand
{
    /// <summary>
    /// Filter by element type (e.g., "Button", "TextBox").
    /// </summary>
    public string? TypeFilter { get; set; }

    /// <summary>
    /// Include elements with no automation ID.
    /// </summary>
    public bool IncludeUnnamed { get; set; }
}

/// <summary>
/// Wait for a condition or duration.
/// </summary>
public class WaitCommand : AutomationCommand
{
    /// <summary>
    /// Milliseconds to wait.
    /// </summary>
    public int Milliseconds { get; set; }

    /// <summary>
    /// Wait for element with this automation ID to exist.
    /// </summary>
    public string? ForElement { get; set; }

    /// <summary>
    /// Maximum wait time in milliseconds when waiting for element.
    /// </summary>
    public int Timeout { get; set; } = 5000;
}

/// <summary>
/// Focus an element or the main window.
/// </summary>
public class FocusCommand : AutomationCommand
{
    /// <summary>
    /// Automation ID of element to focus. If null, focuses the main window.
    /// </summary>
    public string? AutomationId { get; set; }
}

/// <summary>
/// Response from an automation command.
/// </summary>
public class AutomationResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Data { get; set; }

    public static AutomationResponse Ok(string? data = null) => new() { Success = true, Data = data };
    public static AutomationResponse Fail(string error) => new() { Success = false, Error = error };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static AutomationResponse? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AutomationResponse>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
