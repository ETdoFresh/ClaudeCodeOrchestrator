using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClaudeCodeOrchestrator.App.Automation;

/// <summary>
/// Executes automation commands on the UI thread.
/// </summary>
public class AutomationExecutor
{
    public async Task<AutomationResponse> ExecuteAsync(AutomationCommand command)
    {
        try
        {
            return command switch
            {
                ClickCommand click => await ExecuteClickAsync(click),
                TypeTextCommand type => await ExecuteTypeAsync(type),
                PressKeyCommand key => await ExecutePressKeyAsync(key),
                ScreenshotCommand screenshot => await ExecuteScreenshotAsync(screenshot),
                GetElementsCommand elements => await ExecuteGetElementsAsync(elements),
                WaitCommand wait => await ExecuteWaitAsync(wait),
                FocusCommand focus => await ExecuteFocusAsync(focus),
                _ => AutomationResponse.Fail($"Unknown command type: {command.GetType().Name}")
            };
        }
        catch (Exception ex)
        {
            return AutomationResponse.Fail($"Command failed: {ex.Message}");
        }
    }

    private async Task<AutomationResponse> ExecuteClickAsync(ClickCommand cmd)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            Control? target = null;
            Window? targetWindow = null;

            if (!string.IsNullOrEmpty(cmd.AutomationId))
            {
                // Search across all windows (main window + dialogs)
                (target, targetWindow) = FindElementAcrossWindows(cmd.AutomationId);
                if (target is null)
                    return AutomationResponse.Fail($"Element not found: {cmd.AutomationId}");
            }
            else
            {
                targetWindow = GetMainWindow();
            }

            if (targetWindow is null)
                return AutomationResponse.Fail("No window found");

            if (target != null)
            {
                // Get center of element for visual feedback
                var bounds = target.Bounds;
                var screenPos = target.TranslatePoint(new Point(bounds.Width / 2, bounds.Height / 2), targetWindow);

                // Even if TranslatePoint fails (element not in visual tree), still simulate the click
                // This handles menu items that exist logically but aren't rendered yet
                await SimulateClickAsync(targetWindow, target, screenPos ?? new Point(0, 0), cmd.DoubleClick);
                return AutomationResponse.Ok();
            }
            else if (cmd.X.HasValue && cmd.Y.HasValue)
            {
                var point = new Point(cmd.X.Value, cmd.Y.Value);
                var hit = targetWindow.InputHitTest(point) as Control;
                await SimulateClickAsync(targetWindow, hit, point, cmd.DoubleClick);
                return AutomationResponse.Ok();
            }

            return AutomationResponse.Fail("No target specified for click");
        });
    }

    private async Task SimulateClickAsync(Window window, Control? target, Point position, bool doubleClick)
    {
        // Focus the target if it's focusable
        if (target is InputElement inputElement && inputElement.Focusable)
        {
            inputElement.Focus();
        }

        // For buttons, invoke the command directly or raise the Click event
        if (target is Button button)
        {
            if (button.Command?.CanExecute(button.CommandParameter) == true)
            {
                button.Command.Execute(button.CommandParameter);
                return;
            }

            // For buttons without commands, raise the Click routed event
            var clickEventArgs = new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent, button);
            button.RaiseEvent(clickEventArgs);
            return;
        }

        // For menu items, invoke the command or search children for command
        if (target is MenuItem menuItem)
        {
            if (menuItem.Command?.CanExecute(menuItem.CommandParameter) == true)
            {
                menuItem.Command.Execute(menuItem.CommandParameter);
                return;
            }

            // If this is a parent menu, try to find and execute child commands by automation ID
            // This handles the case where sub-menu items aren't in the visual tree yet
            if (menuItem.HasSubMenu && menuItem.ItemCount > 0)
            {
                // Open the menu visually
                menuItem.IsSubMenuOpen = true;
            }
            return;
        }

        // For other controls, try to raise click event
        if (target is InputElement ie)
        {
            ie.Focus();
            // Simulate pointer press/release
            await Task.Delay(50);
        }
    }

    private async Task<AutomationResponse> ExecuteTypeAsync(TypeTextCommand cmd)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Control? target = null;
            Window? targetWindow = null;

            if (!string.IsNullOrEmpty(cmd.AutomationId))
            {
                (target, targetWindow) = FindElementAcrossWindows(cmd.AutomationId);
                if (target is null)
                    return AutomationResponse.Fail($"Element not found: {cmd.AutomationId}");
            }
            else
            {
                targetWindow = GetActiveWindow() ?? GetMainWindow();
            }

            if (targetWindow is null)
                return AutomationResponse.Fail("No window found");

            // Focus target or use currently focused element
            if (target is InputElement ie)
            {
                ie.Focus();
            }

            // Set text directly for TextBox - if we have a target, use it; otherwise use the target we focused
            if (target is TextBox targetTextBox)
            {
                targetTextBox.Text = (targetTextBox.Text ?? "") + cmd.Text;
                return AutomationResponse.Ok();
            }

            // Try to find the currently focused element
            var focusedElement = TopLevel.GetTopLevel(targetWindow)?.FocusManager?.GetFocusedElement();
            if (focusedElement is TextBox textBox)
            {
                textBox.Text = (textBox.Text ?? "") + cmd.Text;
                return AutomationResponse.Ok();
            }

            return AutomationResponse.Fail("No text input focused");
        });
    }

    private async Task<AutomationResponse> ExecutePressKeyAsync(PressKeyCommand cmd)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = GetMainWindow();
            if (window is null)
                return AutomationResponse.Fail("Main window not found");

            // Parse key
            if (!Enum.TryParse<Key>(cmd.Key, true, out var key))
                return AutomationResponse.Fail($"Unknown key: {cmd.Key}");

            // Parse modifiers
            var modifiers = KeyModifiers.None;
            if (!string.IsNullOrEmpty(cmd.Modifiers))
            {
                foreach (var mod in cmd.Modifiers.Split('+'))
                {
                    modifiers |= mod.Trim().ToLower() switch
                    {
                        "ctrl" or "control" => KeyModifiers.Control,
                        "alt" => KeyModifiers.Alt,
                        "shift" => KeyModifiers.Shift,
                        "meta" or "cmd" or "command" => KeyModifiers.Meta,
                        _ => KeyModifiers.None
                    };
                }
            }

            // Create and raise key event
            var focusedElement = TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement() as InputElement ?? window;

            var keyEventArgs = new KeyEventArgs
            {
                Key = key,
                KeyModifiers = modifiers,
                RoutedEvent = InputElement.KeyDownEvent,
                Source = focusedElement
            };

            focusedElement.RaiseEvent(keyEventArgs);

            return AutomationResponse.Ok();
        });
    }

    private async Task<AutomationResponse> ExecuteScreenshotAsync(ScreenshotCommand cmd)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = GetMainWindow();
            if (window is null)
                return AutomationResponse.Fail("Main window not found");

            Control target = window;

            if (!string.IsNullOrEmpty(cmd.AutomationId))
            {
                var element = FindElementByAutomationId(window, cmd.AutomationId);
                if (element is null)
                    return AutomationResponse.Fail($"Element not found: {cmd.AutomationId}");
                target = element;
            }

            // Render to bitmap
            var pixelSize = new PixelSize((int)target.Bounds.Width, (int)target.Bounds.Height);
            if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
                return AutomationResponse.Fail("Element has zero size");

            using var bitmap = new RenderTargetBitmap(pixelSize);
            bitmap.Render(target);

            if (!string.IsNullOrEmpty(cmd.OutputPath))
            {
                // Save to file
                bitmap.Save(cmd.OutputPath);
                return AutomationResponse.Ok(cmd.OutputPath);
            }
            else
            {
                // Return base64
                using var stream = new MemoryStream();
                bitmap.Save(stream);
                var base64 = Convert.ToBase64String(stream.ToArray());
                return AutomationResponse.Ok(base64);
            }
        });
    }

    private async Task<AutomationResponse> ExecuteGetElementsAsync(GetElementsCommand cmd)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var elements = new List<ElementInfo>();

            // Collect elements from all open windows
            foreach (var window in GetAllWindows())
            {
                CollectElements(window, elements, cmd.TypeFilter, cmd.IncludeUnnamed);
            }

            if (elements.Count == 0)
            {
                var mainWindow = GetMainWindow();
                if (mainWindow is null)
                    return AutomationResponse.Fail("No windows found");
            }

            var json = JsonSerializer.Serialize(elements, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });

            return AutomationResponse.Ok(json);
        });
    }

    private void CollectElements(Control control, List<ElementInfo> elements, string? typeFilter, bool includeUnnamed)
    {
        var automationId = AutomationProperties.GetAutomationId(control);
        var typeName = control.GetType().Name;

        var matchesType = string.IsNullOrEmpty(typeFilter) ||
                          typeName.Contains(typeFilter, StringComparison.OrdinalIgnoreCase);

        var hasId = !string.IsNullOrEmpty(automationId);

        if (matchesType && (hasId || includeUnnamed))
        {
            elements.Add(new ElementInfo
            {
                AutomationId = automationId,
                Type = typeName,
                Text = GetElementText(control),
                Bounds = new BoundsInfo
                {
                    X = (int)control.Bounds.X,
                    Y = (int)control.Bounds.Y,
                    Width = (int)control.Bounds.Width,
                    Height = (int)control.Bounds.Height
                },
                IsEnabled = control.IsEnabled,
                IsFocused = control.IsFocused,
                IsVisible = control.IsVisible
            });
        }

        foreach (var child in control.GetVisualChildren().OfType<Control>())
        {
            CollectElements(child, elements, typeFilter, includeUnnamed);
        }
    }

    private string? GetElementText(Control control)
    {
        return control switch
        {
            TextBlock tb => tb.Text,
            Button btn => btn.Content?.ToString(),
            TextBox txt => txt.Text,
            ContentControl cc => cc.Content?.ToString(),
            _ => null
        };
    }

    private async Task<AutomationResponse> ExecuteWaitAsync(WaitCommand cmd)
    {
        if (!string.IsNullOrEmpty(cmd.ForElement))
        {
            // Wait for element to appear (searching all windows)
            var deadline = DateTime.UtcNow.AddMilliseconds(cmd.Timeout);

            while (DateTime.UtcNow < deadline)
            {
                var found = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var (element, _) = FindElementAcrossWindows(cmd.ForElement);
                    return element != null;
                });

                if (found)
                    return AutomationResponse.Ok();

                await Task.Delay(100);
            }

            return AutomationResponse.Fail($"Timeout waiting for element: {cmd.ForElement}");
        }
        else
        {
            // Simple delay
            await Task.Delay(cmd.Milliseconds);
            return AutomationResponse.Ok();
        }
    }

    private async Task<AutomationResponse> ExecuteFocusAsync(FocusCommand cmd)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = GetMainWindow();
            if (window is null)
                return AutomationResponse.Fail("Main window not found");

            if (string.IsNullOrEmpty(cmd.AutomationId))
            {
                window.Activate();
                window.Focus();
                return AutomationResponse.Ok();
            }

            var element = FindElementByAutomationId(window, cmd.AutomationId);
            if (element is null)
                return AutomationResponse.Fail($"Element not found: {cmd.AutomationId}");

            if (element is InputElement ie)
            {
                ie.Focus();
                return AutomationResponse.Ok();
            }

            return AutomationResponse.Fail("Element is not focusable");
        });
    }

    private Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    private IEnumerable<Window> GetAllWindows()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.Windows;
        }
        return [];
    }

    private Window? GetActiveWindow()
    {
        // Return the topmost/focused window, or main window as fallback
        var windows = GetAllWindows().ToList();
        return windows.FirstOrDefault(w => w.IsActive) ?? GetMainWindow();
    }

    /// <summary>
    /// Finds an element by automation ID across all open windows.
    /// </summary>
    private (Control? Element, Window? Window) FindElementAcrossWindows(string automationId)
    {
        foreach (var window in GetAllWindows())
        {
            var element = FindElementByAutomationId(window, automationId);
            if (element != null)
                return (element, window);
        }
        return (null, null);
    }

    private Control? FindElementByAutomationId(Control root, string automationId)
    {
        var id = AutomationProperties.GetAutomationId(root);
        if (id == automationId)
            return root;

        // Search visual children
        foreach (var child in root.GetVisualChildren().OfType<Control>())
        {
            var found = FindElementByAutomationId(child, automationId);
            if (found != null)
                return found;
        }

        // For MenuItems, also search logical children (sub-items not yet in visual tree)
        if (root is MenuItem menuItem)
        {
            foreach (var item in menuItem.Items.OfType<Control>())
            {
                var found = FindElementByAutomationId(item, automationId);
                if (found != null)
                    return found;
            }
        }

        // For ItemsControls like Menu, search items
        if (root is ItemsControl itemsControl)
        {
            foreach (var item in itemsControl.Items.OfType<Control>())
            {
                var found = FindElementByAutomationId(item, automationId);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    private class ElementInfo
    {
        public string? AutomationId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string? Text { get; set; }
        public BoundsInfo Bounds { get; set; } = new();
        public bool IsEnabled { get; set; }
        public bool IsFocused { get; set; }
        public bool IsVisible { get; set; }
    }

    private class BoundsInfo
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
