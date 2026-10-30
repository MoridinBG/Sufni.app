using Sufni.App.Shared.Models;
using System;
using System.Collections.Generic;
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

public partial class MapViewModel : ViewModelBase, IDisposable
{
    private readonly ITileLayerService tileLayerService;
    private readonly IDialogService dialogService;
    private readonly IMapLayerCoordinator coordinator;
    private TileLayerState? layerState;
    private readonly CompositeDisposable subscriptions = new();

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
        IUiThreadDispatcher uiThreadDispatcher,
        TimeProvider timeProvider)
        : base(uiThreadDispatcher, timeProvider: timeProvider)
    {
        this.tileLayerService = tileLayerService;
        this.coordinator = coordinator;
        this.dialogService = dialogService;

        subscriptions.Add(tileLayerService.Observe().Subscribe(state =>
        {
            layerState = state;
            OnPropertyChanged(nameof(AvailableLayers));
            OnPropertyChanged(nameof(SelectedLayer));
        }));
    }

    public async Task InitializeAsync()
    {
        await tileLayerService.InitializeAsync();
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

    public void Dispose() => subscriptions.Dispose();

    [RelayCommand]
    private async Task AddCustomLayer()
    {
        var config = await dialogService.ShowAddTileLayerDialogAsync();
        if (config != null)
        {
            var result = await coordinator.AddAndSelectAsync(config);
            if (result is MapLayerWriteResult.Failed failed)
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
        }
    }
}
