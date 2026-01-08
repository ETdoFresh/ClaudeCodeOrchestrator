using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;

namespace ClaudeCodeOrchestrator.App.ViewModels.Docking;

/// <summary>
/// Base class for dockable tool view models.
/// </summary>
public abstract partial class ToolViewModelBase : Tool
{
}

/// <summary>
/// Base class for dockable document view models.
/// </summary>
public abstract partial class DocumentViewModelBase : Document
{
}
