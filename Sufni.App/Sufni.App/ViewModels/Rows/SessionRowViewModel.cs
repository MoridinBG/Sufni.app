using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.SessionGraph;
using Sufni.App.ViewModels.ItemLists;

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
    private string titleText = string.Empty;
    private string timestampText = string.Empty;
    private string subtitleText = string.Empty;
    private SessionDateGroupKey dateGroupKey = SessionDateGroupKey.NoDate;
    private bool hasSubtitleText;
    private bool isStale;
    private bool hasNoRawSource;
    private bool canRecalculate;

    public string BaseName
    {
        get => baseName;
        private set => SetProperty(ref baseName, value);
    }

    public string TitleText
    {
        get => titleText;
        private set => SetProperty(ref titleText, value);
    }

    public string TimestampText
    {
        get => timestampText;
        private set => SetProperty(ref timestampText, value);
    }

    public string SubtitleText
    {
        get => subtitleText;
        private set
        {
            if (SetProperty(ref subtitleText, value))
            {
                HasSubtitleText = !string.IsNullOrWhiteSpace(value);
            }
        }
    }

    public bool HasSubtitleText
    {
        get => hasSubtitleText;
        private set => SetProperty(ref hasSubtitleText, value);
    }

    public SessionDateGroupKey DateGroupKey
    {
        get => dateGroupKey;
        private set => SetProperty(ref dateGroupKey, value);
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
            parts.Add(FormatDuration(duration));
        }

        if (distanceMeters is { } distance && double.IsFinite(distance) && distance >= 0)
        {
            parts.Add(FormatDistance(distance));
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

    private static string FormatDuration(double durationSeconds)
    {
        var roundedSeconds = Math.Max(0, (long)Math.Round(durationSeconds, MidpointRounding.AwayFromZero));
        var culture = CultureInfo.CurrentCulture;
        var hours = roundedSeconds / 3600;
        var minutes = roundedSeconds % 3600 / 60;
        var seconds = roundedSeconds % 60;

        if (hours > 0)
        {
            return $"{hours.ToString("0", culture)}h {minutes.ToString("00", culture)}m";
        }

        if (minutes > 0)
        {
            return $"{minutes.ToString("0", culture)}m {seconds.ToString("00", culture)}s";
        }

        return $"{seconds.ToString("0", culture)}s";
    }

    private static string FormatDistance(double distanceMeters)
    {
        var culture = CultureInfo.CurrentCulture;
        return distanceMeters >= 1000
            ? $"{(distanceMeters / 1000.0).ToString("0.0", culture)} km"
            : $"{Math.Round(distanceMeters, MidpointRounding.AwayFromZero).ToString("0", culture)} m";
    }

    private static string FormatElevationGain(double ascentMeters, double descentMeters)
    {
        var culture = CultureInfo.CurrentCulture;
        var ascent = Math.Round(ascentMeters, MidpointRounding.AwayFromZero).ToString("0", culture);
        var descent = Math.Round(descentMeters, MidpointRounding.AwayFromZero).ToString("0", culture);
        return $"+{ascent} m / -{descent} m";
    }
}
