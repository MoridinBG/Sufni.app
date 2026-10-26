using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.Stores;
namespace Sufni.App.Segments.ViewModels;

/// <summary>
/// Segments picker of one open recorded session. It lives as long as the
/// session editor. The saved rows are the session's segments that fit the
/// telemetry; the selected row is the one whose range is the analysis range.
/// Strava rows load each time the picker opens, and closing the picker
/// cancels the load.
/// </summary>
public sealed partial class SegmentPickerViewModel : ObservableObject, IDisposable
{
    private const double RangeMatchToleranceSeconds = 0.001;
    private const string ConnectFirstMessage = "Connect with Strava before loading segments.";

    private readonly Guid sessionId;
    private readonly ISegmentCoordinator segmentCoordinator;
    private readonly IStravaSegmentService stravaService;
    private readonly IDialogService dialogService;
    private readonly Action<TelemetryTimeRange?> setAnalysisRange;
    private readonly Action<string> reportError;
    private readonly IDisposable segmentsSubscription;
    private IReadOnlyCollection<SessionSegmentSnapshot> segments = [];
    private long? sessionTimestamp;
    private double? telemetryDurationSeconds;
    private TelemetryTimeRange? analysisRange;
    private CancellationTokenSource? stravaLoad;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedSegments))]
    public partial IReadOnlyList<SavedSegmentRow> SavedSegments { get; private set; } = [];

    [ObservableProperty] public partial bool IsStravaConnected { get; private set; }
    [ObservableProperty] public partial bool IsStravaSegmentsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStravaMessage))]
    public partial string? StravaMessage { get; private set; }

    [ObservableProperty] public partial IReadOnlyList<StravaSegmentRow> StravaSegments { get; private set; } = [];

    public SegmentPickerViewModel(
        Guid sessionId,
        ISessionSegmentStore segmentStore,
        ISegmentCoordinator segmentCoordinator,
        IStravaSegmentService stravaService,
        IDialogService dialogService,
        Action<TelemetryTimeRange?> setAnalysisRange,
        Action<string> reportError)
    {
        this.sessionId = sessionId;
        this.segmentCoordinator = segmentCoordinator;
        this.stravaService = stravaService;
        this.dialogService = dialogService;
        this.setAnalysisRange = setAnalysisRange;
        this.reportError = reportError;
        segmentsSubscription = segmentStore.Connect()
            .Filter(segment => segment.SessionId == sessionId)
            .ToCollection()
            .Subscribe(value =>
            {
                segments = value;
                RefreshSavedSegments();
            });
    }

    public bool CanOpen => telemetryDurationSeconds is not null;
    public bool HasSavedSegments => SavedSegments.Count > 0;
    public bool HasStravaMessage => StravaMessage is not null;

    /// <summary>
    /// The session start, the processed telemetry duration, and the analysis
    /// range of the open session. The picker closes when the telemetry is gone.
    /// </summary>
    public void SetSessionState(long? timestamp, double? durationSeconds, TelemetryTimeRange? range)
    {
        sessionTimestamp = timestamp;
        telemetryDurationSeconds = durationSeconds is > 0 ? durationSeconds : null;
        analysisRange = range;
        OnPropertyChanged(nameof(CanOpen));
        OpenCommand.NotifyCanExecuteChanged();
        if (!CanOpen)
        {
            Close();
        }

        RefreshSavedSegments();
    }

    /// <summary>
    /// Asks for a name and saves <paramref name="range"/> as a segment of the session.
    /// </summary>
    public async Task SaveSelectionAsync(TelemetryTimeRange range)
    {
        var name = await PromptForNameAsync(
            "Save segment",
            "Save",
            segmentCoordinator.NextName(sessionId),
            segmentId: null);
        if (name is not null)
        {
            Report(await segmentCoordinator.CreateAsync(sessionId, name, range.StartSeconds, range.EndSeconds));
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenPicker))]
    private Task Open()
    {
        IsOpen = true;
        return LoadStravaSegmentsAsync();
    }

    private bool CanOpenPicker() => CanOpen && !IsOpen;

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        CancelStravaLoad();
    }

    [RelayCommand]
    private Task Toggle()
    {
        if (IsOpen)
        {
            Close();
            return Task.CompletedTask;
        }

        return CanOpenPicker() ? Open() : Task.CompletedTask;
    }

    [RelayCommand]
    private void SelectSavedSegment(SavedSegmentRow row)
    {
        setAnalysisRange(row.IsSelected ? null : row.Range);
        Close();
    }

    [RelayCommand]
    private async Task RenameSavedSegment(SavedSegmentRow row)
    {
        var name = await PromptForNameAsync("Rename segment", "Rename", row.Name, row.Id);
        if (name is not null)
        {
            Report(await segmentCoordinator.RenameAsync(row.Id, name));
        }
    }

    [RelayCommand]
    private async Task DeleteSavedSegment(SavedSegmentRow row)
    {
        if (await dialogService.ShowConfirmationAsync("Delete saved segment?", $"Delete \"{row.Name}\"?"))
        {
            Report(await segmentCoordinator.DeleteAsync(row.Id));
        }
    }

    [RelayCommand]
    private void SelectStravaSegment(StravaSegmentRow row)
    {
        setAnalysisRange(row.Range);
        Close();
    }

    [RelayCommand]
    private async Task ConnectWithStrava()
    {
        var dialog = new StravaConnectDialogViewModel(stravaService, await stravaService.GetConnectionSnapshotAsync());
        await dialogService.ShowContentDialogAsync(
            dialog,
            new DialogOptions(
                Title: "Connect with Strava",
                Width: 520,
                Height: 520,
                MinWidth: 420,
                MinHeight: 360,
                CanResize: false));
        await LoadStravaSegmentsAsync();
    }

    [RelayCommand]
    private async Task ResetStravaCredentials()
    {
        CancelStravaLoad();
        await stravaService.ResetCredentialsAsync();
        ShowStravaDisconnected(ConnectFirstMessage);
    }

    private async Task LoadStravaSegmentsAsync()
    {
        CancelStravaLoad();
        var load = new CancellationTokenSource();
        stravaLoad = load;
        StravaSegments = [];
        StravaMessage = null;
        try
        {
            var connection = await stravaService.GetConnectionSnapshotAsync(load.Token);
            IsStravaConnected = connection.IsConnected;
            if (!connection.IsConnected)
            {
                StravaMessage = ConnectFirstMessage;
                return;
            }

            if (sessionTimestamp is not { } timestamp || telemetryDurationSeconds is not { } duration)
            {
                StravaMessage = "Session timing is not available for Strava segments.";
                return;
            }

            IsStravaSegmentsLoading = true;
            ApplyStravaResult(await stravaService.FindSegmentsAsync(
                DateTimeOffset.FromUnixTimeSeconds(timestamp),
                duration,
                load.Token));
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            StravaMessage = $"Could not load Strava segments: {ex.Message}";
        }
        finally
        {
            if (stravaLoad == load)
            {
                IsStravaSegmentsLoading = false;
                stravaLoad = null;
            }

            load.Dispose();
        }
    }

    private void ApplyStravaResult(StravaSegmentFetchResult result)
    {
        switch (result)
        {
            case StravaSegmentFetchResult.Loaded loaded:
                StravaSegments = [.. loaded.Segments.Select(segment =>
                    new StravaSegmentRow(segment.Name, segment.AnalysisRange, segment.DistanceMeters))];
                break;
            case StravaSegmentFetchResult.NotConnected:
                ShowStravaDisconnected(ConnectFirstMessage);
                break;
            case StravaSegmentFetchResult.ReconnectRequired:
                ShowStravaDisconnected("Strava authorization expired. Connect with Strava again.");
                break;
            case StravaSegmentFetchResult.NoMatchingActivity:
                StravaMessage = "No overlapping Strava activity was found.";
                break;
            case StravaSegmentFetchResult.NoUsableSegments:
                StravaMessage = "No usable Strava segment efforts were found.";
                break;
            case StravaSegmentFetchResult.RateLimited:
                StravaMessage = "Strava rate limit reached.";
                break;
            case StravaSegmentFetchResult.Failed failed:
                StravaMessage = $"Could not load Strava segments: {failed.Message}";
                break;
        }
    }

    private void ShowStravaDisconnected(string message)
    {
        IsStravaConnected = false;
        StravaSegments = [];
        StravaMessage = message;
    }

    private void CancelStravaLoad()
    {
        stravaLoad?.Cancel();
        stravaLoad = null;
        IsStravaSegmentsLoading = false;
    }

    private async Task<string?> PromptForNameAsync(string title, string confirmText, string name, Guid? segmentId)
    {
        var prompt = new SegmentNamePromptViewModel(
            title,
            confirmText,
            name,
            candidate => segmentCoordinator.ValidateName(sessionId, candidate, segmentId));
        await dialogService.ShowContentDialogAsync(
            prompt,
            new DialogOptions(
                Title: title,
                Width: 420,
                Height: 220,
                MinWidth: 320,
                MinHeight: 180,
                CanResize: false));
        return prompt.ConfirmedName;
    }

    private void Report(SegmentChangeResult result)
    {
        if (result is SegmentChangeResult.Failed failed)
        {
            reportError(failed.ErrorMessage);
        }
    }

    private void RefreshSavedSegments()
    {
        var rows = new List<SavedSegmentRow>();
        if (telemetryDurationSeconds is { } duration)
        {
            foreach (var segment in segments.OrderBy(segment => segment.StartSeconds))
            {
                if (segment.StartSeconds >= 0 &&
                    segment.EndSeconds <= duration &&
                    TelemetryTimeRange.TryCreate(segment.StartSeconds, segment.EndSeconds, out var range))
                {
                    rows.Add(new SavedSegmentRow(segment.Id, segment.Name, range, IsSelected(segment)));
                }
            }
        }

        SavedSegments = rows;
    }

    private bool IsSelected(SessionSegmentSnapshot segment) =>
        analysisRange is { } range &&
        Math.Abs(range.StartSeconds - segment.StartSeconds) <= RangeMatchToleranceSeconds &&
        Math.Abs(range.EndSeconds - segment.EndSeconds) <= RangeMatchToleranceSeconds;

    public void Dispose()
    {
        segmentsSubscription.Dispose();
        CancelStravaLoad();
    }
}
