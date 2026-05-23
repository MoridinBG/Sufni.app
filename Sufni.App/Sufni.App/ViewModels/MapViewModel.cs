using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Models;
using Sufni.App.Services;

namespace Sufni.App.ViewModels;

public partial class MapViewModel : ViewModelBase, IDisposable
{
    private readonly ITileLayerService tileLayerService;
    private readonly IDialogService dialogService;
    private readonly CompositeDisposable subscriptions = new();

    [ObservableProperty]
    private TileLayerConfig? selectedLayer;

    [ObservableProperty]
    private List<TrackPoint>? fullTrackPoints;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSessionTrackPoints))]
    private List<TrackPoint>? sessionTrackPoints;

    [ObservableProperty]
    private TrackTimeRange? timelineContext;

    public bool HasSessionTrackPoints => SessionTrackPoints?.Count > 0;

    public ObservableCollection<TileLayerConfig> AvailableLayers => tileLayerService.AvailableLayers;

    public MapViewModel(
        ITileLayerService tileLayerService,
        IDialogService dialogService,
        IUiThreadDispatcher uiThreadDispatcher)
        : base(uiThreadDispatcher)
    {
        this.tileLayerService = tileLayerService;
        this.dialogService = dialogService;

        // BehaviorSubject replays the current value to late subscribers, so a
        // VM constructed after the service is initialized still picks up the
        // selected layer on subscribe.
        subscriptions.Add(tileLayerService.SelectedLayerChanges
            .Subscribe(layer => SelectedLayer = layer));
    }

    public async Task InitializeAsync()
    {
        await tileLayerService.InitializeAsync();
    }

    public async Task SelectLayerAsync(TileLayerConfig? value)
    {
        if (value is null) return;

        SelectedLayer = value;

        try
        {
            await tileLayerService.SetSelectedLayerAsync(value);
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Map layer preference could not be saved: {e.Message}");
        }
    }

    public void Dispose() => subscriptions.Dispose();

    [RelayCommand]
    private async Task AddCustomLayer()
    {
        var config = await dialogService.ShowAddTileLayerDialogAsync();
        if (config != null)
        {
            await tileLayerService.AddCustomLayerAsync(config);
            await SelectLayerAsync(config);
        }
    }
}
