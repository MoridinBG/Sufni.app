using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.SessionGraph;

namespace Sufni.App.ViewModels.Rows;

/// <summary>
/// Binding state for one recorded-session summary row.
/// It translates summary metadata and staleness into display text, completion
/// state, timestamp state, and no-raw/stale indicators.
/// </summary>
public sealed class SessionRowViewModel : ListItemRowViewModelBase
{
    private readonly SessionCoordinator sessionCoordinator;
    private readonly Action<SessionRowViewModel> requestDelete;
    private readonly Func<SessionRowViewModel, Task> requestRecalculate;

    public Guid Id { get; private set; }
    public long Updated { get; private set; }
    private string baseName = string.Empty;
    private bool isStale;
    private bool hasNoRawSource;
    private bool canRecalculate;

    public string BaseName
    {
        get => baseName;
        private set => SetProperty(ref baseName, value);
    }

    public bool IsStale
    {
        get => isStale;
        private set => SetProperty(ref isStale, value);
    }

    public bool HasNoRawSource
    {
        get => hasNoRawSource;
        private set => SetProperty(ref hasNoRawSource, value);
    }

    public bool CanRecalculate
    {
        get => canRecalculate;
        private set
        {
            if (SetProperty(ref canRecalculate, value))
            {
                RecalculateCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IAsyncRelayCommand RecalculateCommand { get; }

    public SessionRowViewModel(
        RecordedSessionSummary summary,
        SessionCoordinator sessionCoordinator,
        Action<SessionRowViewModel> requestDelete,
        Func<SessionRowViewModel, Task> requestRecalculate)
    {
        this.sessionCoordinator = sessionCoordinator;
        this.requestDelete = requestDelete;
        this.requestRecalculate = requestRecalculate;
        RecalculateCommand = new AsyncRelayCommand(RecalculateAsync, () => CanRecalculate);
        Update(summary);
    }

    public void Update(RecordedSessionSummary summary)
    {
        Id = summary.Id;
        Updated = summary.Updated;
        BaseName = summary.Name;
        IsStale = summary.Staleness.IsStale;
        HasNoRawSource = summary.Staleness is SessionStaleness.MissingRawSource;
        CanRecalculate = summary.Staleness.CanManualRecompute;
        Name = summary.Staleness switch
        {
            SessionStaleness.MissingRawSource => $"{BaseName} (No Raw)",
            { IsStale: true } => $"{BaseName} (Stale)",
            _ => BaseName,
        };
        Timestamp = summary.Timestamp is null
            ? null
            : DateTimeOffset.FromUnixTimeSeconds(summary.Timestamp.Value).LocalDateTime;
        IsComplete = summary.HasProcessedData;
    }

    protected override async Task OpenPageAsync()
    {
        await sessionCoordinator.OpenEditAsync(Id);
    }

    protected override void UndoableDelete()
    {
        requestDelete(this);
    }

    private Task RecalculateAsync()
    {
        return requestRecalculate(this);
    }
}
