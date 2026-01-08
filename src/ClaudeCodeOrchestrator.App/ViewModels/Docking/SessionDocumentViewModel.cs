using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeCodeOrchestrator.Core.Models;

namespace ClaudeCodeOrchestrator.App.ViewModels.Docking;

/// <summary>
/// Document view model for a session tab.
/// </summary>
public partial class SessionDocumentViewModel : DocumentViewModelBase
{
    private string _sessionId = string.Empty;
    private SessionState _state = SessionState.Starting;
    private string _inputText = string.Empty;
    private bool _isProcessing;
    private decimal _totalCost;
    private string _worktreeBranch = string.Empty;

    public string SessionId
    {
        get => _sessionId;
        set => SetProperty(ref _sessionId, value);
    }

    public SessionState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    public string InputText
    {
        get => _inputText;
        set
        {
            if (SetProperty(ref _inputText, value))
            {
                SendMessageCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set
        {
            if (SetProperty(ref _isProcessing, value))
            {
                SendMessageCommand.NotifyCanExecuteChanged();
                InterruptCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public decimal TotalCost
    {
        get => _totalCost;
        set => SetProperty(ref _totalCost, value);
    }

    public string WorktreeBranch
    {
        get => _worktreeBranch;
        set => SetProperty(ref _worktreeBranch, value);
    }

    public ObservableCollection<MessageViewModel> Messages { get; } = new();

    public SessionDocumentViewModel()
    {
        Id = Guid.NewGuid().ToString();
        Title = "New Session";
        CanClose = true;
        CanFloat = true;
    }

    public SessionDocumentViewModel(string sessionId, string title, string worktreeBranch)
    {
        SessionId = sessionId;
        Id = sessionId;
        Title = title.Length > 30 ? title[..27] + "..." : title;
        WorktreeBranch = worktreeBranch;
        CanClose = true;
        CanFloat = true;
    }

    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;

        var text = InputText;
        InputText = string.Empty;

        Messages.Add(new UserMessageViewModel { Content = text });
        IsProcessing = true;

        // TODO: Wire up to session service
        await Task.CompletedTask;
    }

    private bool CanSendMessage() => !IsProcessing && !string.IsNullOrWhiteSpace(InputText);

    [RelayCommand(CanExecute = nameof(CanInterrupt))]
    private async Task InterruptAsync()
    {
        // TODO: Wire up to session service
        await Task.CompletedTask;
    }

    private bool CanInterrupt() => IsProcessing;
}
