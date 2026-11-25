using Sufni.App.Terrain.Formatting;
using Sufni.Telemetry;
using System.Linq;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.Terrain.Plots;
using Sufni.App.Terrain.Models;
using Sufni.App.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Shared.Base;
namespace Sufni.App.MapsAndTracks.ViewModels;

public sealed record MapLegendEntry(string Label, Avalonia.Media.Color Colour);

public partial class MapViewModel : ViewModelBase, IDisposable
{
    private readonly IDialogService dialogService;
    private readonly IMapLayerCoordinator coordinator;
    private TileLayerState? layerState;
    private readonly CompositeDisposable subscriptions = new();

    private readonly Action<MapTrackColoring>? selectColoring;
    private TerrainPresentation? terrain;
    private MapTrackColoring coloring;

    public static IReadOnlyList<MapTrackColoring> Colorings { get; } = Enum.GetValues<MapTrackColoring>();
    public bool SupportsTerrainColoring => selectColoring is not null;
    public bool HasTerrain => terrain is not null;

    public MapTrackColoring TrackColoring
    {
        get => coloring;
        set { if (HasTerrain && value != coloring) selectColoring?.Invoke(value); }
    }

    public ImmutableArray<MapTerrainRun> TerrainRuns => coloring switch
    {
        MapTrackColoring.Roughness when terrain is not null => Runs(terrain.RoughnessRuns, SufniThemes.SignalSeries.Terrain.ColorFor),
        MapTrackColoring.Grade when terrain is not null => Runs(terrain.GradeRuns, SufniThemes.SignalSeries.Terrain.ColorFor),
        _ => []
    };

    private static ImmutableArray<MapTerrainRun> Runs<TClass>(ImmutableArray<TerrainRun<TClass>> runs,
        Func<TClass, Avalonia.Media.Color> colour) where TClass : struct, Enum =>
        [.. runs.Select(run => new MapTerrainRun(run.StartSeconds, run.EndSeconds, colour(run.Class)))];

    /// <summary>The classes of the selected terrain colouring, then the style of ground without a class.</summary>
    public IReadOnlyList<MapLegendEntry> TerrainLegend => TerrainRuns.IsEmpty ? [] : coloring switch
    {
        MapTrackColoring.Roughness => Legend(Enum.GetValues<RoughnessClass>().Select(v =>
            new MapLegendEntry(TerrainClassText.Name(v), SufniThemes.SignalSeries.Terrain.ColorFor(v)))),
        _ => Legend(Enum.GetValues<GradeBand>().Select(v =>
            new MapLegendEntry(TerrainClassText.Name(v), SufniThemes.SignalSeries.Terrain.ColorFor(v))))
    };

    private static MapLegendEntry[] Legend(IEnumerable<MapLegendEntry> classes) =>
        [.. classes, new("No class", SufniThemes.SignalSeries.Terrain.Unclassified)];

    internal void ApplyTerrain(MapTrackColoring selected, TerrainPresentation? presentation)
    {
        if (coloring == selected && ReferenceEquals(terrain, presentation)) return;
        var hadTerrain = HasTerrain;
        var previous = coloring;
        terrain = presentation;
        coloring = selected;
        if (hadTerrain != HasTerrain) OnPropertyChanged(nameof(HasTerrain));
        if (previous != selected) OnPropertyChanged(nameof(TrackColoring));
        OnPropertyChanged(nameof(TerrainRuns));
        OnPropertyChanged(nameof(TerrainLegend));
    }

    public IMapGeometryFactory GeometryFactory { get; }

    public TileLayerConfig? SelectedLayer => layerState?.SelectedLayer;

    [ObservableProperty]
    public partial List<TrackPoint>? FullTrackPoints { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSessionTrackPoints))]
    public partial List<TrackPoint>? SessionTrackPoints { get; set; }

    [ObservableProperty]
    public partial TrackTimeRange? TimelineContext { get; set; }

    public bool HasSessionTrackPoints => SessionTrackPoints?.Count > 0;

    public IReadOnlyList<TileLayerConfig> AvailableLayers => layerState?.AvailableLayers ?? [];

    public MapViewModel(
        ITileLayerService tileLayerService,
        IMapLayerCoordinator coordinator,
        IDialogService dialogService,
        IMapGeometryFactory geometryFactory,
        IUiThreadDispatcher uiThreadDispatcher,
        TimeProvider timeProvider, Action<MapTrackColoring>? selectColoring = null)
        : base(uiThreadDispatcher, timeProvider: timeProvider)
    {
        GeometryFactory = geometryFactory;
        this.selectColoring = selectColoring;
        this.coordinator = coordinator;
        this.dialogService = dialogService;

        subscriptions.Add(tileLayerService.Observe().Subscribe(state =>
        {
            layerState = state;
            OnPropertyChanged(nameof(AvailableLayers));
            OnPropertyChanged(nameof(SelectedLayer));
        }));
    }

    public async Task SelectLayerAsync(TileLayerConfig? value)
    {
        if (value is null || value.Id == SelectedLayer?.Id) return;
        var result = await coordinator.SelectAsync(value.Id);
        if (result is MapLayerWriteResult.Failed failed)
        {
            ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
            OnPropertyChanged(nameof(SelectedLayer));
        }
    }

    public void ReportRenderingFailure(FailurePresentation failure) => ErrorMessages.Add(failure);

    public void Dispose() => subscriptions.Dispose();

    [RelayCommand]
    private async Task AddCustomLayer()
    {
        var draft = new AddTileLayerViewModel();
        await dialogService.ShowContentDialogAsync(draft, AddTileLayerViewModel.DialogOptions);
        if (draft.ConfirmedLayer is { } config)
        {
            var result = await coordinator.AddAndSelectAsync(config);
            if (result is MapLayerWriteResult.Failed failed)
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
        }
    }
}
