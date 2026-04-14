using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly ILiveDaqSharedStream? sharedStream;
    private readonly CancellableOperation loadOperation = new();
    private SidebarState acceptedSidebarState;
    private ILiveDaqSharedStreamLease? streamLease;
    private ILiveDaqSharedStreamLease? configurationLockLease;
    private bool isApplyingAcceptedState;

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
        HookSidebarChangeTracking();
        acceptedSidebarState = CreateDefaultSidebarState();
        ApplyAcceptedSidebarState();
    }

    public LiveSessionDetailViewModel(
        LiveDaqSessionContext context,
        ILiveDaqSharedStream sharedStream,
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
        this.sharedStream = sharedStream;

        var timeline = new SessionTimelineLinkViewModel();
        graphWorkspace = new LiveSessionGraphWorkspaceViewModel(timeline);
        mediaWorkspace = new LiveSessionMediaWorkspaceViewModel(tileLayerService, dialogService, timeline);

        HookSidebarChangeTracking();
        acceptedSidebarState = CreateDefaultSidebarState();
        ApplyAcceptedSidebarState();
        RefreshSharedStreamState();
    }

    partial void OnDescriptionTextChanged(string? value)
    {
        if (isApplyingAcceptedState)
        {
            return;
        }

        EvaluateDirtiness();
    }

    [RelayCommand]
    private async Task Loaded()
    {
        await mediaWorkspace.InitializeAsync();

        if (sharedStream is null)
        {
            return;
        }

        if (streamLease is null)
        {
            streamLease = sharedStream.AcquireLease();
        }

        if (configurationLockLease is null)
        {
            configurationLockLease = sharedStream.AcquireConfigurationLock();
        }

        EnsureScopedSubscription(disposables =>
        {
            disposables.Add(sharedStream.States.Subscribe(_ => RequestSharedStreamRefresh()));
        });

        RefreshSharedStreamState();

        var cancellationToken = loadOperation.Start();
        try
        {
            await sharedStream.EnsureStartedAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        RefreshSharedStreamState();
    }

    [RelayCommand]
    private async Task Unloaded()
    {
        await DeactivateAsync();
    }

    protected override async Task CloseImplementation()
    {
        await DeactivateAsync();
    }

    protected override void EvaluateDirtiness()
    {
        IsDirty = CaptureSidebarState() != acceptedSidebarState;
    }

    protected override bool CanSave()
    {
        return false;
    }

    protected override bool CanReset()
    {
        EvaluateDirtiness();
        return IsDirty;
    }

    protected override Task ResetImplementation()
    {
        ApplyAcceptedSidebarState();
        return Task.CompletedTask;
    }

    private async Task DeactivateAsync()
    {
        loadOperation.Cancel();
        DisposeScopedSubscriptions();

        if (configurationLockLease is not null)
        {
            await configurationLockLease.DisposeAsync();
            configurationLockLease = null;
        }

        if (streamLease is not null)
        {
            await streamLease.DisposeAsync();
            streamLease = null;
        }
    }

    private void HookSidebarChangeTracking()
    {
        PropertyChanged += OnEditorPropertyChanged;
        ForkSettings.PropertyChanged += OnSuspensionSettingsChanged;
        ShockSettings.PropertyChanged += OnSuspensionSettingsChanged;
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isApplyingAcceptedState || e.PropertyName != nameof(Name))
        {
            return;
        }

        EvaluateDirtiness();
    }

    private void OnSuspensionSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isApplyingAcceptedState)
        {
            return;
        }

        EvaluateDirtiness();
    }

    private void RequestSharedStreamRefresh()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshSharedStreamState();
            return;
        }

        Dispatcher.UIThread.Post(RefreshSharedStreamState, DispatcherPriority.Background);
    }

    private void RefreshSharedStreamState()
    {
        var state = sharedStream?.CurrentState ?? LiveDaqSharedStreamState.Empty;
        if (state.SessionHeader is { } header)
        {
            Timestamp = header.SessionStartUtc.LocalDateTime;
        }

        ControlState = new LiveSessionControlState(
            ConnectionState: state.ConnectionState,
            LastError: state.LastError,
            SessionHeader: state.SessionHeader,
            CaptureDuration: CalculateCaptureDuration(state.SessionHeader),
            TravelQueueDepth: 0,
            ImuQueueDepth: 0,
            GpsQueueDepth: 0,
            TravelDroppedBatches: 0,
            ImuDroppedBatches: 0,
            GpsDroppedBatches: 0,
            CanSave: false,
            IsSaving: false,
            IsResetting: false);
    }

    private SidebarState CreateDefaultSidebarState()
    {
        return new SidebarState(
            Name: CreateDefaultName(DateTimeOffset.Now),
            DescriptionText: null,
            Fork: SuspensionSettingsState.Empty,
            Shock: SuspensionSettingsState.Empty);
    }

    private SidebarState CaptureSidebarState()
    {
        return new SidebarState(
            Name: Name,
            DescriptionText: DescriptionText,
            Fork: SuspensionSettingsState.From(ForkSettings),
            Shock: SuspensionSettingsState.From(ShockSettings));
    }

    private void ApplyAcceptedSidebarState()
    {
        isApplyingAcceptedState = true;
        try
        {
            Name = acceptedSidebarState.Name;
            DescriptionText = acceptedSidebarState.DescriptionText;
            acceptedSidebarState.Fork.ApplyTo(ForkSettings);
            acceptedSidebarState.Shock.ApplyTo(ShockSettings);
        }
        finally
        {
            isApplyingAcceptedState = false;
        }

        EvaluateDirtiness();
    }

    private static TimeSpan CalculateCaptureDuration(LiveSessionHeader? sessionHeader)
    {
        if (sessionHeader is null)
        {
            return TimeSpan.Zero;
        }

        var duration = DateTimeOffset.UtcNow - sessionHeader.SessionStartUtc;
        return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
    }

    private static string CreateDefaultName(DateTimeOffset localTime)
    {
        return $"Live Session {localTime.LocalDateTime:dd-MM-yyyy HH:mm:ss}";
    }

    private sealed record SidebarState(
        string? Name,
        string? DescriptionText,
        SuspensionSettingsState Fork,
        SuspensionSettingsState Shock);

    private sealed record SuspensionSettingsState(
        string? SpringRate,
        uint? HighSpeedCompression,
        uint? LowSpeedCompression,
        uint? LowSpeedRebound,
        uint? HighSpeedRebound)
    {
        public static readonly SuspensionSettingsState Empty = new(null, null, null, null, null);

        public static SuspensionSettingsState From(SuspensionSettings settings)
        {
            return new SuspensionSettingsState(
                settings.SpringRate,
                settings.HighSpeedCompression,
                settings.LowSpeedCompression,
                settings.LowSpeedRebound,
                settings.HighSpeedRebound);
        }

        public void ApplyTo(SuspensionSettings settings)
        {
            settings.SpringRate = SpringRate;
            settings.HighSpeedCompression = HighSpeedCompression;
            settings.LowSpeedCompression = LowSpeedCompression;
            settings.LowSpeedRebound = LowSpeedRebound;
            settings.HighSpeedRebound = HighSpeedRebound;
        }
    }
}