using System;
using System.Collections.Generic;
using System.Globalization;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionGraph;

using Sufni.App.Sessions.Coordination;
using Sufni.App.Shared.Base;
using Sufni.App.Sessions.Lists.ViewModels.ItemLists;
using Sufni.App.Shared.Formatting;
namespace Sufni.App.Sessions.Lists.ViewModels.Rows;

/// <summary>
/// Binding state for one recorded-session summary row.
/// It translates summary metadata and staleness into display text, completion
/// state, timestamp state, and no-raw/stale indicators.
/// </summary>
public sealed class SessionRowViewModel : ListItemRowViewModelBase
{
    private readonly ISessionCoordinator sessionCoordinator;
    private readonly Action<SessionRowViewModel> requestDelete;
    private readonly Func<SessionRowViewModel, Task> requestRecalculate;
    private readonly IRecordedSessionListExtensionService? listExtensionService;
    private RecordedSessionSummary? summary;

    public Guid Id { get; private set; }
    public long Updated { get; private set; }

    public string BaseName
    {
        get => field;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public string TitleText
    {
        get => field;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public string TimestampText
    {
        get => field;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public string SubtitleText
    {
        get => field;
        private set
        {
            if (SetProperty(ref field, value))
            {
                HasSubtitleText = !string.IsNullOrWhiteSpace(value);
            }
        }
    } = string.Empty;

    public bool HasSubtitleText
    {
        get => field;
        private set => SetProperty(ref field, value);
    }

    public SessionDateGroupKey DateGroupKey
    {
        get => field;
        private set => SetProperty(ref field, value);
    } = SessionDateGroupKey.NoDate;

    public bool IsStale
    {
        get => field;
        private set => SetProperty(ref field, value);
    }

    public bool HasNoRawSource
    {
        get => field;
        private set => SetProperty(ref field, value);
    }

    public bool CanRecalculate
    {
        get => field;
        private set
        {
            if (SetProperty(ref field, value))
            {
                RecalculateCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IAsyncRelayCommand RecalculateCommand { get; }
    public ObservableCollection<RecordedSessionListIndicatorContribution> Indicators { get; } = [];
    public ObservableCollection<RecordedSessionListActionContribution> Actions { get; } = [];

    public SessionRowViewModel(
        RecordedSessionSummary summary,
        ISessionCoordinator sessionCoordinator,
        Action<SessionRowViewModel> requestDelete,
        Func<SessionRowViewModel, Task> requestRecalculate,
        IRecordedSessionListExtensionService? listExtensionService = null)
    {
        this.sessionCoordinator = sessionCoordinator;
        this.requestDelete = requestDelete;
        this.requestRecalculate = requestRecalculate;
        this.listExtensionService = listExtensionService;
        RecalculateCommand = new AsyncRelayCommand(RecalculateAsync, () => CanRecalculate);
        Update(summary);
    }

    public void Update(RecordedSessionSummary summary)
    {
        this.summary = summary;
        Id = summary.Id;
        Updated = summary.Updated;
        BaseName = summary.Name;
        Name = BaseName;
        IsStale = summary.Staleness.IsStale;
        HasNoRawSource = summary.Staleness is SessionStaleness.MissingRawSource;
        CanRecalculate = summary.Staleness.CanManualRecompute;
        TitleText = summary.Staleness switch
        {
            SessionStaleness.MissingRawSource => $"{BaseName} (No Raw)",
            { IsStale: true } => $"{BaseName} (Stale)",
            _ => BaseName,
        };

        var localTimestamp = summary.Timestamp is null
            ? (DateTime?)null
            : DateTimeOffset.FromUnixTimeSeconds(summary.Timestamp.Value).LocalDateTime;
        Timestamp = localTimestamp;
        TimestampText = FormatTimestamp(localTimestamp);
        DateGroupKey = localTimestamp is { } value
            ? new SessionDateGroupKey(DateOnly.FromDateTime(value))
            : SessionDateGroupKey.NoDate;
        SubtitleText = FormatSubtitle(
            summary.DurationSeconds,
            summary.DistanceMeters,
            summary.AscentMeters,
            summary.DescentMeters);
        IsComplete = summary.HasProcessedData;
        RefreshExtensionContributions(summary);
    }

    public void RefreshExtensionContributions()
    {
        if (summary is not null)
        {
            RefreshExtensionContributions(summary);
        }
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

    private void RefreshExtensionContributions(RecordedSessionSummary summary)
    {
        Indicators.Clear();
        Actions.Clear();
        if (listExtensionService is null)
        {
            return;
        }

        foreach (var contribution in listExtensionService.CreateIndicators(summary))
        {
            Indicators.Add(contribution);
        }

        foreach (var contribution in listExtensionService.CreateActions(summary))
        {
            Actions.Add(contribution);
        }
    }

    private static string FormatTimestamp(DateTime? timestamp)
    {
        if (timestamp is null)
        {
            return "No date";
        }

        var culture = CultureInfo.CurrentCulture;
        return timestamp.Value.ToString(culture.DateTimeFormat.ShortTimePattern, culture);
    }

    private static string FormatSubtitle(
        double? durationSeconds,
        double? distanceMeters,
        double? ascentMeters,
        double? descentMeters)
    {
        var parts = new List<string>();

        if (durationSeconds is { } duration && double.IsFinite(duration) && duration >= 0)
        {
            parts.Add(UnitsFormatter.FormatDuration(TimeSpan.FromSeconds(duration)));
        }

        if (distanceMeters is { } distance && double.IsFinite(distance) && distance >= 0)
        {
            parts.Add(UnitsFormatter.FormatDistance(distance));
        }

        if (ascentMeters is { } ascent &&
            descentMeters is { } descent &&
            double.IsFinite(ascent) &&
            double.IsFinite(descent) &&
            ascent >= 0 &&
            descent >= 0)
        {
            parts.Add(FormatElevationGain(ascent, descent));
        }

        return string.Join(" | ", parts);
    }

    private static string FormatElevationGain(double ascentMeters, double descentMeters)
    {
        var ascent = UnitsFormatter.FormatNumber(Math.Round(ascentMeters, MidpointRounding.AwayFromZero), 0);
        var descent = UnitsFormatter.FormatNumber(Math.Round(descentMeters, MidpointRounding.AwayFromZero), 0);
        return $"+{ascent} m / -{descent} m";
    }
}
