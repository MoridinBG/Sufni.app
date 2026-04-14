using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Models;
using Sufni.App.Coordinators;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.SessionDetails;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.SessionPages;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Editors;

public sealed partial class LiveSessionDetailViewModel : TabPageViewModelBase,
    ISessionStatisticsWorkspace,
    ISessionSidebarWorkspace,
    ILiveSessionControlsWorkspace
{
    private readonly LiveSessionGraphWorkspaceViewModel graphWorkspace;
    private readonly LiveSessionMediaWorkspaceViewModel mediaWorkspace;
    private readonly ILiveSessionService? liveSessionService;
    private readonly ISessionCoordinator? sessionCoordinator;
    private readonly DispatcherTimer controlRefreshTimer;
    private bool hasLoaded;
    private bool isSaving = false;
    private bool isResetting;

    public string IdentityKey { get; }
    public Guid SetupId { get; }
    public string? SetupName { get; }
    public Guid BikeId { get; }
    public string? BikeName { get; }

    public ILiveSessionGraphWorkspace GraphWorkspace => graphWorkspace;
    public ISessionMediaWorkspace MediaWorkspace => mediaWorkspace;
    public SuspensionSettings ForkSettings { get; } = new();
    public SuspensionSettings ShockSettings { get; } = new();

    [ObservableProperty]
    private string? descriptionText;

    [ObservableProperty]
    private TelemetryData? telemetryData;

    [ObservableProperty]
    private SessionDamperPercentages damperPercentages = new(null, null, null, null, null, null, null, null);

    [ObservableProperty]
    private LiveSessionControlState controlState = LiveSessionControlState.Empty;

    public LiveSessionDetailViewModel()
    {
        IdentityKey = string.Empty;
        graphWorkspace = new LiveSessionGraphWorkspaceViewModel();
        mediaWorkspace = new LiveSessionMediaWorkspaceViewModel();
        controlRefreshTimer = CreateControlRefreshTimer();
        Name = CreateDefaultName(DateTimeOffset.Now);
    }

    public LiveSessionDetailViewModel(
        LiveDaqSessionContext context,
        ILiveSessionService liveSessionService,
        ISessionCoordinator sessionCoordinator,
        ITileLayerService tileLayerService,
        IShellCoordinator shell,
        IDialogService dialogService)
        : base(shell, dialogService)
    {
        IdentityKey = context.IdentityKey;
        SetupId = context.SetupId;
        SetupName = context.SetupName;
        BikeId = context.BikeId;
        BikeName = context.BikeName;
        this.liveSessionService = liveSessionService;
        this.sessionCoordinator = sessionCoordinator;

        var timeline = new SessionTimelineLinkViewModel();
        graphWorkspace = new LiveSessionGraphWorkspaceViewModel(timeline);
        mediaWorkspace = new LiveSessionMediaWorkspaceViewModel(tileLayerService, dialogService, timeline);
        graphWorkspace.Attach(liveSessionService.GraphBatches);
        controlRefreshTimer = CreateControlRefreshTimer();
        Name = CreateDefaultName(DateTimeOffset.Now);

        liveSessionService.Snapshots.Subscribe(RequestPresentationRefresh);
        ApplyPresentation(liveSessionService.Current);
    }

    partial void OnDescriptionTextChanged(string? value)
    {
    }

    [RelayCommand]
    private async Task Loaded()
    {
        controlRefreshTimer.Start();
        if (hasLoaded)
        {
            return;
        }

        hasLoaded = true;
        await mediaWorkspace.InitializeAsync();

        if (liveSessionService is null)
        {
            return;
        }

        await liveSessionService.EnsureAttachedAsync();
        ApplyPresentation(liveSessionService.Current);
    }

    [RelayCommand]
    private async Task Unloaded()
    {
        controlRefreshTimer.Stop();
        await Task.CompletedTask;
    }

    protected override async Task CloseImplementation()
    {
        controlRefreshTimer.Stop();

        if (liveSessionService is not null)
        {
            await liveSessionService.DisposeAsync();
        }
    }

    protected override void EvaluateDirtiness()
    {
        IsDirty = ControlState.CanSave;
    }

    protected override bool CanSave()
    {
        return ControlState.CanSave && !isSaving && !isResetting;
    }

    protected override bool CanReset()
    {
        return ControlState.CanSave && !isSaving && !isResetting;
    }

    protected override async Task ResetImplementation()
    {
        if (liveSessionService is null)
        {
            return;
        }

        isResetting = true;
        RefreshCommandState();

        try
        {
            await liveSessionService.ResetCaptureAsync();
            graphWorkspace.Timeline.Reset();
            ApplyPresentation(liveSessionService.Current);
        }
        finally
        {
            isResetting = false;
            RefreshCommandState();
        }
    }

    protected override async Task SaveImplementation()
    {
        if (liveSessionService is null || sessionCoordinator is null)
        {
            return;
        }

        isSaving = true;
        ControlState = ApplyControlFlags(ControlState);
        RefreshCommandState();

        var shouldRefreshAutoName = string.IsNullOrWhiteSpace(Name) || IsAutoGeneratedName(Name);

        try
        {
            var capture = await liveSessionService.PrepareCaptureForSaveAsync();
            var saveTime = DateTimeOffset.Now;
            var sessionName = shouldRefreshAutoName ? CreateDefaultName(saveTime) : Name!;
            var session = new Session(
                id: Guid.NewGuid(),
                name: sessionName,
                description: DescriptionText ?? string.Empty,
                setup: SetupId,
                timestamp: capture.TelemetryCapture.Metadata.Timestamp)
            {
                FrontSpringRate = ForkSettings.SpringRate,
                FrontHighSpeedCompression = ForkSettings.HighSpeedCompression,
                FrontLowSpeedCompression = ForkSettings.LowSpeedCompression,
                FrontLowSpeedRebound = ForkSettings.LowSpeedRebound,
                FrontHighSpeedRebound = ForkSettings.HighSpeedRebound,
                RearSpringRate = ShockSettings.SpringRate,
                RearHighSpeedCompression = ShockSettings.HighSpeedCompression,
                RearLowSpeedCompression = ShockSettings.LowSpeedCompression,
                RearLowSpeedRebound = ShockSettings.LowSpeedRebound,
                RearHighSpeedRebound = ShockSettings.HighSpeedRebound,
            };

            var result = await sessionCoordinator.SaveLiveCaptureAsync(session, capture);
            switch (result)
            {
                case LiveSessionSaveResult.Saved:
                    if (shouldRefreshAutoName)
                    {
                        Name = CreateDefaultName(DateTimeOffset.Now);
                    }
                    break;

                case LiveSessionSaveResult.Failed failed:
                    ErrorMessages.Add($"Live session could not be saved: {failed.ErrorMessage}");
                    break;
            }
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Live session could not be saved: {e.Message}");
        }
        finally
        {
            isSaving = false;
            ControlState = ApplyControlFlags(ControlState);
            RefreshCommandState();
            Dispatcher.UIThread.Post(() =>
            {
                EvaluateDirtiness();
                RefreshCommandState();
            }, DispatcherPriority.Background);
        }
    }

    private static string CreateDefaultName(DateTimeOffset localTime)
    {
        return $"Live Session {localTime.LocalDateTime:dd-MM-yyyy HH:mm:ss}";
    }

    private static bool IsAutoGeneratedName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !name.StartsWith("Live Session ", StringComparison.Ordinal))
        {
            return false;
        }

        return DateTime.TryParseExact(
            name["Live Session ".Length..],
            "dd-MM-yyyy HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }

    private void RequestPresentationRefresh(LiveSessionPresentationSnapshot snapshot)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyPresentation(snapshot);
            return;
        }

        Dispatcher.UIThread.Post(() => ApplyPresentation(snapshot), DispatcherPriority.Background);
    }

    private void ApplyPresentation(LiveSessionPresentationSnapshot snapshot)
    {
        TelemetryData = snapshot.StatisticsTelemetry;
        DamperPercentages = snapshot.DamperPercentages;
        mediaWorkspace.SetTrackPoints(snapshot.SessionTrackPoints);

        if (snapshot.Controls.SessionHeader is { } header)
        {
            Timestamp = header.SessionStartUtc.LocalDateTime;
        }

        ControlState = ApplyControlFlags(snapshot.Controls);
        EvaluateDirtiness();
        RefreshCommandState();
    }

    private LiveSessionControlState ApplyControlFlags(LiveSessionControlState controlState)
    {
        return controlState with
        {
            CaptureDuration = RefreshCaptureDuration(controlState.SessionHeader),
            IsSaving = isSaving,
            IsResetting = isResetting,
        };
    }

    private LiveSessionControlState RefreshControlState()
    {
        ControlState = ApplyControlFlags(ControlState);
        EvaluateDirtiness();
        return ControlState;
    }

    private void RefreshCommandState()
    {
        SaveCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
    }

    private DispatcherTimer CreateControlRefreshTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        timer.Tick += (_, _) => RefreshControlState();
        return timer;
    }

    private static TimeSpan RefreshCaptureDuration(LiveSessionHeader? header)
    {
        if (header is null)
        {
            return TimeSpan.Zero;
        }

        var duration = DateTimeOffset.UtcNow - header.SessionStartUtc;
        return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
    }
}