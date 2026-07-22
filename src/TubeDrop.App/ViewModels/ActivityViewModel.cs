using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TubeDrop.Core.Journal;

namespace TubeDrop.App.ViewModels;

public sealed record SessionRow(long SessionId, string StartedAt, int OperationCount);

public sealed record OperationRow(long OperationId, string Type, string Timestamp, string Status);

public partial class ActivityViewModel : ObservableObject
{
    private readonly JournalStore _journal;
    private readonly UndoService _undoService;
    private readonly ILogger<ActivityViewModel> _logger;

    [ObservableProperty] private SessionRow? _selectedSession;
    [ObservableProperty] private string _statusMessage = "";

    public ObservableCollection<SessionRow> Sessions { get; } = [];
    public ObservableCollection<OperationRow> Operations { get; } = [];

    public ActivityViewModel(JournalStore journal, UndoService undoService, ILogger<ActivityViewModel> logger)
    {
        _journal = journal;
        _undoService = undoService;
        _logger = logger;
    }

    public void Refresh()
    {
        Sessions.Clear();
        foreach (var (sessionId, startedAt, count) in _journal.GetSessions())
        {
            Sessions.Add(new SessionRow(sessionId, startedAt.LocalDateTime.ToString("g"), count));
        }

        Operations.Clear();
        SelectedSession = Sessions.FirstOrDefault();
    }

    partial void OnSelectedSessionChanged(SessionRow? value)
    {
        Operations.Clear();
        if (value is null)
        {
            return;
        }

        foreach (var op in _journal.GetSessionOperations(value.SessionId))
        {
            Operations.Add(new OperationRow(op.Id, op.Type, op.Timestamp.LocalDateTime.ToString("T"), op.Status.ToString()));
        }
    }

    [RelayCommand]
    private async Task UndoOperationAsync(long operationId)
    {
        var outcome = await _undoService.UndoOperationAsync(operationId);
        StatusMessage = outcome.Message;
        _logger.LogInformation("Undo op {Op}: {Ok} — {Message}", operationId, outcome.Succeeded, outcome.Message);
        OnSelectedSessionChanged(SelectedSession);
    }

    [RelayCommand]
    private async Task UndoSessionAsync()
    {
        if (SelectedSession is null)
        {
            return;
        }

        var results = await _undoService.UndoSessionAsync(SelectedSession.SessionId);
        var ok = results.Count(r => r.Outcome.Succeeded);
        StatusMessage = $"Undid {ok}/{results.Count} operations";
        Refresh();
    }
}
