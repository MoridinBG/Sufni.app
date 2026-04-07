using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScottPlot;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Plots;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels.SessionPages;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Editors;

/// <summary>
/// Editor view model for a session's detail tab. Constructed by
/// <c>SessionCoordinator</c> from a <see cref="SessionSnapshot"/>; the
/// snapshot's <c>Updated</c> value is kept as
/// <see cref="BaselineUpdated"/> for optimistic conflict detection at
/// save time. Save and delete route through the coordinator. The local
/// mobile telemetry-fetch path goes through
/// <see cref="ISessionCoordinator.EnsureTelemetryDataAvailableAsync"/>.
///
/// The editor subscribes to <c>ISessionStore.Watch(Id)</c> in its
/// <c>Loaded</c> command and disposes the subscription in
/// <c>Unloaded</c>. The Watch handler is gated on
/// <c>initialLoadCompleted</c> so the initial load does not race the
/// reaction triggered by the editor's own
/// <c>EnsureTelemetryDataAvailableAsync</c> call. After the initial
/// load, subsequent Watch emissions (sync arrival, recalculation)
/// trigger an automatic <c>LoadTelemetryData</c>. See
/// "Telemetry-arrival semantics" in REFACTOR-PLAN.md.
/// </summary>
public sealed partial class SessionDetailViewModel : TabPageViewModelBase, IEditorActions
{
    public Guid Id { get; private set; }
    public long BaselineUpdated { get; private set; }

    public string Description => NotesPage.Description ?? "";

    // Explicit interface implementation: the generated commands are
    // IAsyncRelayCommand[<T>] which C# does not implicitly satisfy a
    // non-generic IRelayCommand interface property with.
    IRelayCommand IEditorActions.OpenPreviousPageCommand => OpenPreviousPageCommand;
    IRelayCommand IEditorActions.SaveCommand => SaveCommand;
    IRelayCommand IEditorActions.ResetCommand => ResetCommand;
    IRelayCommand IEditorActions.DeleteCommand => DeleteCommand;
    IRelayCommand IEditorActions.FakeDeleteCommand => FakeDeleteCommand;

    #region Private fields

    private readonly ISessionCoordinator? sessionCoordinator;
    private readonly ISessionStore? sessionStore;
    private readonly IDatabaseService databaseService;
    private Session session;
    private SpringPageViewModel SpringPage { get; } = new();
    private BalancePageViewModel BalancePage { get; } = new();

    private CompositeDisposable? subscriptions;
    private bool initialLoadCompleted;
    private bool lastObservedHasProcessedData;
    private Rect? lastLoadedBounds;

    #endregion Private fields

    #region Public fields

    public DamperPageViewModel DamperPage { get; } = new();
    public NotesPageViewModel NotesPage { get; } = new();

    #endregion Public fields

    #region Observable properties

    [ObservableProperty] private TelemetryData? telemetryData;
    [ObservableProperty] private List<TrackPoint>? fullTrackPoints;
    [ObservableProperty] private List<TrackPoint>? trackPoints;
    [ObservableProperty] private string? videoUrl;
    [ObservableProperty] private double? mapVideoWidth;
    [ObservableProperty] private bool isComplete;
    public ObservableCollection<PageViewModelBase> Pages { get; }

    #endregion Observable properties

    partial void OnTelemetryDataChanged(TelemetryData? value)
    {
        IsComplete = value != null;
    }

    #region Private methods

    private async Task LoadTelemetryData()
    {
        TelemetryData = await databaseService.GetSessionPsstAsync(Id);
        if (TelemetryData is null)
        {
            // Desktop sync server case: the metadata row arrived via
            // SyncPush but the psst blob upload (PatchSessionData) has
            // not landed yet. Leave TelemetryData null and let the
            // Watch subscription installed in Loaded fire LoadTelemetryData
            // again when the blob arrives. OnTelemetryDataChanged maps
            // null → IsComplete = false so the UI shows "loading" state.
            return;
        }

        if (TelemetryData.Front.Present)
        {
            var fvb = TelemetryData.CalculateVelocityBands(SuspensionType.Front, 200);
            DamperPage.FrontHsrPercentage = fvb.HighSpeedRebound;
            DamperPage.FrontLsrPercentage = fvb.LowSpeedRebound;
            DamperPage.FrontLscPercentage = fvb.LowSpeedCompression;
            DamperPage.FrontHscPercentage = fvb.HighSpeedCompression;
        }

        if (TelemetryData.Rear.Present)
        {
            var rvb = TelemetryData.CalculateVelocityBands(SuspensionType.Rear, 200);
            DamperPage.RearHsrPercentage = rvb.HighSpeedRebound;
            DamperPage.RearLsrPercentage = rvb.LowSpeedRebound;
            DamperPage.RearLscPercentage = rvb.LowSpeedCompression;
            DamperPage.RearHscPercentage = rvb.HighSpeedCompression;
        }
    }

    private async Task LoadTrack()
    {
        Debug.Assert(TelemetryData is not null);

        session.FullTrack ??= await databaseService.AssociateSessionWithTrackAsync(Id);
        if (session.FullTrack is not null)
        {
            var fullTrack = await databaseService.GetAsync<Track>(session.FullTrack.Value);
            FullTrackPoints = fullTrack.Points;
            MapVideoWidth = 400;

            TrackPoints = await databaseService.GetSessionTrackAsync(Id);
            if (TrackPoints is null)
            {
                var start = TelemetryData.Metadata.Timestamp;
                var end = start + (int)Math.Ceiling(TelemetryData.Metadata.Duration);
                TrackPoints = fullTrack.GenerateSessionTrack(TelemetryData.Metadata.Timestamp, end);
                await databaseService.PatchSessionTrackAsync(Id, TrackPoints);
            }
        }
    }

    #endregion

    #region Private methods [cache, mobile-only]

    private async Task<bool> LoadCache()
    {
        var cache = await databaseService.GetSessionCacheAsync(Id);
        if (cache is null)
        {
            return false;
        }

        SpringPage.FrontTravelHistogram = cache.FrontTravelHistogram;
        SpringPage.RearTravelHistogram = cache.RearTravelHistogram;

        DamperPage.FrontVelocityHistogram = cache.FrontVelocityHistogram;
        DamperPage.RearVelocityHistogram = cache.RearVelocityHistogram;
        DamperPage.FrontHscPercentage = cache.FrontHscPercentage;
        DamperPage.RearHscPercentage = cache.RearHscPercentage;
        DamperPage.FrontLscPercentage = cache.FrontLscPercentage;
        DamperPage.RearLscPercentage = cache.RearLscPercentage;
        DamperPage.FrontLsrPercentage = cache.FrontLsrPercentage;
        DamperPage.RearLsrPercentage = cache.RearLsrPercentage;
        DamperPage.FrontHsrPercentage = cache.FrontHsrPercentage;
        DamperPage.RearHsrPercentage = cache.RearHsrPercentage;

        if (cache.CompressionBalance is not null)
        {
            BalancePage.CompressionBalance = cache.CompressionBalance;
            BalancePage.ReboundBalance = cache.ReboundBalance;
        }
        else
        {
            Pages.Remove(BalancePage);
        }

        return true;
    }

    private async Task CreateCache(object? bounds)
    {
        if (sessionCoordinator is not null)
        {
            await sessionCoordinator.EnsureTelemetryDataAvailableAsync(Id);
        }
        await LoadTelemetryData();
        Debug.Assert(TelemetryData is not null);

        var b = (Rect)bounds!;
        var (width, height) = ((int)b.Width, (int)(b.Height / 2.0));
        var sessionCache = new SessionCache
        {
            SessionId = Id
        };

        if (TelemetryData.Front.Present)
        {
            var fth = new TravelHistogramPlot(new Plot(), SuspensionType.Front);
            fth.LoadTelemetryData(TelemetryData);
            sessionCache.FrontTravelHistogram = fth.Plot.GetSvgXml(width, height);
            Dispatcher.UIThread.Post(() => { SpringPage.FrontTravelHistogram = sessionCache.FrontTravelHistogram; });

            var fvh = new VelocityHistogramPlot(new Plot(), SuspensionType.Front);
            fvh.LoadTelemetryData(TelemetryData);
            sessionCache.FrontVelocityHistogram = fvh.Plot.GetSvgXml(width - 64, 478);
            Dispatcher.UIThread.Post(() => { DamperPage.FrontVelocityHistogram = sessionCache.FrontVelocityHistogram; });

            var fvb = TelemetryData.CalculateVelocityBands(SuspensionType.Front, 200);
            sessionCache.FrontHsrPercentage = fvb.HighSpeedRebound;
            sessionCache.FrontLsrPercentage = fvb.LowSpeedRebound;
            sessionCache.FrontLscPercentage = fvb.LowSpeedCompression;
            sessionCache.FrontHscPercentage = fvb.HighSpeedCompression;
            Dispatcher.UIThread.Post(() =>
            {
                DamperPage.FrontHsrPercentage = fvb.HighSpeedRebound;
                DamperPage.FrontLsrPercentage = fvb.LowSpeedRebound;
                DamperPage.FrontLscPercentage = fvb.LowSpeedCompression;
                DamperPage.FrontHscPercentage = fvb.HighSpeedCompression;
            });
        }

        if (TelemetryData.Rear.Present)
        {
            var rth = new TravelHistogramPlot(new Plot(), SuspensionType.Rear);
            rth.LoadTelemetryData(TelemetryData);
            sessionCache.RearTravelHistogram = rth.Plot.GetSvgXml(width, height);
            Dispatcher.UIThread.Post(() => { SpringPage.RearTravelHistogram = sessionCache.RearTravelHistogram; });

            var rvh = new VelocityHistogramPlot(new Plot(), SuspensionType.Rear);
            rvh.LoadTelemetryData(TelemetryData);
            sessionCache.RearVelocityHistogram = rvh.Plot.GetSvgXml(width - 64, 478);
            Dispatcher.UIThread.Post(() => { DamperPage.RearVelocityHistogram = sessionCache.RearVelocityHistogram; });

            var rvb = TelemetryData.CalculateVelocityBands(SuspensionType.Rear, 200);
            sessionCache.RearHsrPercentage = rvb.HighSpeedRebound;
            sessionCache.RearLsrPercentage = rvb.LowSpeedRebound;
            sessionCache.RearLscPercentage = rvb.LowSpeedCompression;
            sessionCache.RearHscPercentage = rvb.HighSpeedCompression;
            Dispatcher.UIThread.Post(() =>
            {
                DamperPage.RearHsrPercentage = rvb.HighSpeedRebound;
                DamperPage.RearLsrPercentage = rvb.LowSpeedRebound;
                DamperPage.RearLscPercentage = rvb.LowSpeedCompression;
                DamperPage.RearHscPercentage = rvb.HighSpeedCompression;
            });
        }

        if (TelemetryData.Front.Present && TelemetryData.Rear.Present)
        {

            var cb = new BalancePlot(new Plot(), BalanceType.Compression);
            cb.LoadTelemetryData(TelemetryData);
            sessionCache.CompressionBalance = cb.Plot.GetSvgXml(width, height);
            Dispatcher.UIThread.Post(() => { BalancePage.CompressionBalance = sessionCache.CompressionBalance; });

            var rb = new BalancePlot(new Plot(), BalanceType.Rebound);
            rb.LoadTelemetryData(TelemetryData);
            sessionCache.ReboundBalance = rb.Plot.GetSvgXml(width, height);
            Dispatcher.UIThread.Post(() => { BalancePage.ReboundBalance = sessionCache.ReboundBalance; });
        }
        else
        {
            Dispatcher.UIThread.Post(() => { Pages.Remove(BalancePage); });
        }

        await databaseService.PutSessionCacheAsync(sessionCache);
    }

    #endregion [cache, mobile-only]

    #region Constructors

    public SessionDetailViewModel()
    {
        sessionCoordinator = null;
        sessionStore = null;
        databaseService = null!;
        session = new Session();
        Pages = [SpringPage, DamperPage, BalancePage, NotesPage];
    }

    internal SessionDetailViewModel(
        SessionSnapshot snapshot,
        ISessionCoordinator sessionCoordinator,
        ISessionStore sessionStore,
        IDatabaseService databaseService,
        IShellCoordinator shell,
        IDialogService dialogService)
        : base(shell, dialogService)
    {
        this.sessionCoordinator = sessionCoordinator;
        this.sessionStore = sessionStore;
        this.databaseService = databaseService;
        session = SessionFromSnapshot(snapshot);
        Id = snapshot.Id;
        BaselineUpdated = snapshot.Updated;
        IsComplete = snapshot.HasProcessedData;
        lastObservedHasProcessedData = snapshot.HasProcessedData;
        Pages = [SpringPage, DamperPage, BalancePage, NotesPage];

        NotesPage.ForkSettings.PropertyChanged += (_, _) => EvaluateDirtiness();
        NotesPage.ShockSettings.PropertyChanged += (_, _) => EvaluateDirtiness();
        NotesPage.PropertyChanged += (_, _) => EvaluateDirtiness();

        ResetImplementation();
    }

    #endregion

    #region Private methods

    private static Session SessionFromSnapshot(SessionSnapshot snapshot)
    {
        var s = new Session(snapshot.Id, snapshot.Name, snapshot.Description, snapshot.SetupId, snapshot.Timestamp)
        {
            FullTrack = snapshot.FullTrackId,
            FrontSpringRate = snapshot.FrontSpringRate,
            FrontHighSpeedCompression = snapshot.FrontHighSpeedCompression,
            FrontLowSpeedCompression = snapshot.FrontLowSpeedCompression,
            FrontLowSpeedRebound = snapshot.FrontLowSpeedRebound,
            FrontHighSpeedRebound = snapshot.FrontHighSpeedRebound,
            RearSpringRate = snapshot.RearSpringRate,
            RearHighSpeedCompression = snapshot.RearHighSpeedCompression,
            RearLowSpeedCompression = snapshot.RearLowSpeedCompression,
            RearLowSpeedRebound = snapshot.RearLowSpeedRebound,
            RearHighSpeedRebound = snapshot.RearHighSpeedRebound,
            HasProcessedData = snapshot.HasProcessedData,
            Updated = snapshot.Updated,
        };
        return s;
    }

    #endregion Private methods

    #region TabPageViewModelBase overrides

    protected override void EvaluateDirtiness()
    {
        IsDirty =
            Name != session.Name ||
            NotesPage.IsDirty(session);
    }

    protected override async Task SaveImplementation()
    {
        if (sessionCoordinator is null) return;

        var newSession = new Session(
            id: session.Id,
            name: Name ?? $"session #{session.Id}",
            description: NotesPage.Description ?? $"session #{session.Id}",
            setup: session.Setup,
            timestamp: session.Timestamp)
        {
            FrontSpringRate = NotesPage.ForkSettings.SpringRate,
            FrontHighSpeedCompression = NotesPage.ForkSettings.HighSpeedCompression,
            FrontLowSpeedCompression = NotesPage.ForkSettings.LowSpeedCompression,
            FrontLowSpeedRebound = NotesPage.ForkSettings.LowSpeedRebound,
            FrontHighSpeedRebound = NotesPage.ForkSettings.HighSpeedRebound,
            RearSpringRate = NotesPage.ShockSettings.SpringRate,
            RearHighSpeedCompression = NotesPage.ShockSettings.HighSpeedCompression,
            RearLowSpeedCompression = NotesPage.ShockSettings.LowSpeedCompression,
            RearLowSpeedRebound = NotesPage.ShockSettings.LowSpeedRebound,
            RearHighSpeedRebound = NotesPage.ShockSettings.HighSpeedRebound,
            HasProcessedData = IsComplete,
            FullTrack = session.FullTrack,
        };

        var result = await sessionCoordinator.SaveAsync(newSession, BaselineUpdated);
        switch (result)
        {
            case SessionSaveResult.Saved saved:
                session = newSession;
                session.Updated = saved.NewBaselineUpdated;
                BaselineUpdated = saved.NewBaselineUpdated;
                IsDirty = false;

                Debug.Assert(App.Current is not null);
                if (!App.Current.IsDesktop)
                {
                    OpenPreviousPage();
                }
                break;

            case SessionSaveResult.Conflict conflict:
                var reload = await dialogService.ShowConfirmationAsync(
                    "Session changed elsewhere",
                    "This session has been updated from another source. Discard your changes and reload?");
                if (reload)
                {
                    session = SessionFromSnapshot(conflict.CurrentSnapshot);
                    BaselineUpdated = conflict.CurrentSnapshot.Updated;
                    IsComplete = conflict.CurrentSnapshot.HasProcessedData;
                    lastObservedHasProcessedData = conflict.CurrentSnapshot.HasProcessedData;
                    await ResetImplementation();
                    EvaluateDirtiness();
                }
                break;

            case SessionSaveResult.Failed failed:
                ErrorMessages.Add($"Session could not be saved: {failed.ErrorMessage}");
                break;
        }
    }

    protected override Task ResetImplementation()
    {
        Id = session.Id;
        Name = session.Name;

        NotesPage.Description = session.Description;
        NotesPage.ForkSettings.SpringRate = session.FrontSpringRate;
        NotesPage.ForkSettings.HighSpeedCompression = session.FrontHighSpeedCompression;
        NotesPage.ForkSettings.LowSpeedCompression = session.FrontLowSpeedCompression;
        NotesPage.ForkSettings.LowSpeedRebound = session.FrontLowSpeedRebound;
        NotesPage.ForkSettings.HighSpeedRebound = session.FrontHighSpeedRebound;

        NotesPage.ShockSettings.SpringRate = session.RearSpringRate;
        NotesPage.ShockSettings.HighSpeedCompression = session.RearHighSpeedCompression;
        NotesPage.ShockSettings.LowSpeedCompression = session.RearLowSpeedCompression;
        NotesPage.ShockSettings.LowSpeedRebound = session.RearLowSpeedRebound;
        NotesPage.ShockSettings.HighSpeedRebound = session.RearHighSpeedRebound;

        Timestamp = DateTimeOffset.FromUnixTimeSeconds(session.Timestamp ?? 0).LocalDateTime;

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Delete(bool navigateBack)
    {
        if (sessionCoordinator is null) return;

        var result = await sessionCoordinator.DeleteAsync(Id);
        switch (result.Outcome)
        {
            case SessionDeleteOutcome.Deleted:
                if (navigateBack) OpenPreviousPage();
                break;
            case SessionDeleteOutcome.Failed:
                ErrorMessages.Add($"Session could not be deleted: {result.ErrorMessage}");
                break;
        }
    }

    [RelayCommand]
    private void FakeDelete()
    {
        // Exists so the editor button strip can bind to a delete command.
    }

    #endregion TabPageViewModelBase overrides

    #region Commands

    [RelayCommand]
    private async Task Loaded(Rect? bounds = null)
    {
        // Remembered for the Watch handler so the mobile branch can
        // re-run CreateCache (which needs the scroll viewer bounds for
        // SVG sizing) when telemetry arrives later.
        lastLoadedBounds = bounds;

        try
        {
            Debug.Assert(App.Current is not null);
            if (App.Current.IsDesktop)
            {
                // Desktop is the sync receiver: it does not have an
                // HttpApiService.ServerUrl to download the psst from,
                // so don't go through EnsureTelemetryDataAvailableAsync
                // here. If the blob is missing the Watch subscription
                // installed below will trigger another LoadTelemetryData
                // when the client uploads it. LoadTrack is only called
                // once telemetry actually loaded — it dereferences
                // TelemetryData.Metadata.
                await LoadTelemetryData();
                if (TelemetryData is not null)
                {
                    await LoadTrack();
                }
            }
            else if (!await LoadCache())
            {
                await CreateCache(bounds);
            }
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not load session data: {e.Message}");
        }
        finally
        {
            // Subscribe AFTER the initial load so the editor's own
            // EnsureTelemetryDataAvailableAsync call (which upserts the
            // store) doesn't race the Watch handler with the data we're
            // about to load ourselves. See "Telemetry-arrival semantics"
            // in REFACTOR-PLAN.md.
            if (sessionStore is not null && subscriptions is null)
            {
                subscriptions = new CompositeDisposable();
                subscriptions.Add(sessionStore.Watch(Id).Subscribe(OnSnapshotChanged));
            }
            initialLoadCompleted = true;
        }
    }

    [RelayCommand]
    private void Unloaded()
    {
        subscriptions?.Dispose();
        subscriptions = null;
    }

    private void OnSnapshotChanged(SessionSnapshot snapshot)
    {
        if (snapshot is null) return;
        if (!initialLoadCompleted) return;
        if (snapshot.HasProcessedData == lastObservedHasProcessedData) return;

        lastObservedHasProcessedData = snapshot.HasProcessedData;
        if (!snapshot.HasProcessedData) return;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                Debug.Assert(App.Current is not null);
                if (App.Current.IsDesktop)
                {
                    // Desktop binds plot views directly to TelemetryData,
                    // so reloading the blob is enough.
                    await LoadTelemetryData();
                    if (TelemetryData is not null)
                    {
                        await LoadTrack();
                    }
                }
                else
                {
                    // Mobile binds to the SVG histograms / balance plots
                    // populated by CreateCache. Re-run that to refresh
                    // them. Skip if Loaded never ran (no bounds known yet).
                    if (lastLoadedBounds is not null)
                    {
                        await CreateCache(lastLoadedBounds);
                    }
                }
            }
            catch (Exception e)
            {
                ErrorMessages.Add($"Could not refresh session data: {e.Message}");
            }
        });
    }

    #endregion
}
