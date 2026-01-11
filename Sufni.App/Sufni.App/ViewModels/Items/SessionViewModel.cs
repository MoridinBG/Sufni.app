using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ScottPlot;
using Sufni.App.Models;
using Sufni.App.Plots;
using Sufni.App.Services;
using Sufni.App.ViewModels.SessionPages;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Items;

public sealed partial class SessionViewModel : ItemViewModelBase
{
    public bool IsInDatabase;
    public string Description => NotesPage.Description ?? "";
    
    #region Private fields

    private Session session;
    private SpringPageViewModel SpringPage { get; } = new();
    private BalancePageViewModel BalancePage { get; } = new();

    #endregion Private fields
    
    #region Public fields

    public DamperPageViewModel DamperPage { get; } = new();
    public NotesPageViewModel NotesPage { get; } = new();
    public MapViewModel MapViewModel { get; }

    #endregion Public fields

    #region Observable properties
    
    [ObservableProperty] private TelemetryData? telemetryData;
    [ObservableProperty] private List<TrackPoint>? fullTrackPoints;
    [ObservableProperty] private List<TrackPoint>? trackPoints;
    [ObservableProperty] private string? videoUrl;
    [ObservableProperty] private double? mapVideoWidth;
    public ObservableCollection<PageViewModelBase> Pages { get; }

    #endregion Observable properties

    partial void OnTelemetryDataChanged(TelemetryData? value)
    {
        IsComplete = value != null;
    }

    #region Private methods

    private async Task LoadTelemetryData()
    {
        var databaseService = App.Current?.Services?.GetService<IDatabaseService>();
        Debug.Assert(databaseService != null);

        TelemetryData = await databaseService.GetSessionPsstAsync(Id);
        if (TelemetryData is null)
        {
            throw new Exception("Database error");
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
        var databaseService = App.Current?.Services?.GetService<IDatabaseService>();
        Debug.Assert(databaseService != null);
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

            MapViewModel.FullTrackPoints = FullTrackPoints;
            MapViewModel.SessionTrackPoints = TrackPoints;
        }
    }

    #endregion
    
    #region Private methods [cache, mobile-only]

    private async Task FetchTelemetryDataIfNeeded()
    {
        if (IsInDatabase) return;

        var httpApiService = App.Current?.Services?.GetService<IHttpApiService>();
        var databaseService = App.Current?.Services?.GetService<IDatabaseService>();
        Debug.Assert(httpApiService != null, nameof(httpApiService) + " != null");
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        var psst = await httpApiService.GetSessionPsstAsync(Id) ?? throw new Exception("Session data could not be downloaded from server.");
        await databaseService.PatchSessionPsstAsync(Id, psst);
        session.HasProcessedData = true;
        IsComplete = true;
    }

    private async Task<bool> LoadCache()
    {
        var databaseService = App.Current?.Services?.GetService<IDatabaseService>();
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

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
        await FetchTelemetryDataIfNeeded();
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

        var databaseService = App.Current?.Services?.GetService<IDatabaseService>();
        Debug.Assert(databaseService != null);
        await databaseService.PutSessionCacheAsync(sessionCache);
    }

    #endregion [cache, mobile-only]

    #region Constructors

    public SessionViewModel()
    {
        session = new Session();
        IsInDatabase = false;
        Pages = [SpringPage, DamperPage, BalancePage, NotesPage];
        
        var tileLayerService = App.Current?.Services?.GetService<ITileLayerService>();
        var dialogService = App.Current?.Services?.GetService<IDialogService>();
        Debug.Assert(tileLayerService != null);
        Debug.Assert(dialogService != null);
        MapViewModel = new MapViewModel(tileLayerService, dialogService);
        _ = MapViewModel.InitializeAsync();
    }

    public SessionViewModel(Session session, bool fromDatabase)
    {
        this.session = session;
        IsInDatabase = fromDatabase;
        IsComplete = session.HasProcessedData;
        Pages = [SpringPage, DamperPage, BalancePage, NotesPage];

        NotesPage.ForkSettings.PropertyChanged += (_, _) => EvaluateDirtiness();
        NotesPage.ShockSettings.PropertyChanged += (_, _) => EvaluateDirtiness();
        NotesPage.PropertyChanged += (_, _) => EvaluateDirtiness();
        
        var tileLayerService = App.Current?.Services?.GetService<ITileLayerService>();
        var dialogService = App.Current?.Services?.GetService<IDialogService>();
        Debug.Assert(tileLayerService != null);
        Debug.Assert(dialogService != null);
        MapViewModel = new MapViewModel(tileLayerService, dialogService);
        _ = MapViewModel.InitializeAsync();

        ResetImplementation();
    }

    #endregion

    #region TabPageViewModelBase overrides

    protected override void EvaluateDirtiness()
    {
        IsDirty =
            !IsInDatabase ||
            Name != session.Name ||
            NotesPage.IsDirty(session);
    }

    protected override async Task SaveImplementation()
    {
        var databaseService = App.Current?.Services?.GetService<IDatabaseService>();
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        try
        {
            var newSession = new Session(
                id: session.Id,
                name: Name ?? $"session #{session.Id}",
                description: NotesPage.Description ?? $"session #{session.Id}",
                setup: session.Setup)
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
            };

            await databaseService.PutSessionAsync(newSession);
            session = newSession;
            IsDirty = false;
            IsInDatabase = true;
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Session could not be saved: {e.Message}");
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

    #endregion TabPageViewModelBase overrides

    #region Commands

    [RelayCommand]
    private async Task Loaded(Rect? bounds = null)
    {
        try
        {
            Debug.Assert(App.Current is not null);
            if (App.Current.IsDesktop)
            {
                await LoadTelemetryData();
                await LoadTrack();
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
    }

    #endregion
}