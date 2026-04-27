using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Models;
using Sufni.App.Services;

namespace Sufni.App.ViewModels;

public partial class MapViewModel : ViewModelBase
{
    private readonly ITileLayerService tileLayerService;
    private readonly IDialogService dialogService;

    [ObservableProperty]
    private TileLayerConfig? selectedLayer;

    [ObservableProperty]
    private List<TrackPoint>? fullTrackPoints;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSessionTrackPoints))]
    private List<TrackPoint>? sessionTrackPoints;

    public bool HasSessionTrackPoints => SessionTrackPoints?.Count > 0;

    public ObservableCollection<TileLayerConfig> AvailableLayers => tileLayerService.AvailableLayers;

    public MapViewModel(ITileLayerService tileLayerService, IDialogService dialogService)
    {
        this.tileLayerService = tileLayerService;
        this.dialogService = dialogService;
    }

    public async Task InitializeAsync()
    {
        await tileLayerService.InitializeAsync();
        SelectedLayer = tileLayerService.SelectedLayer;
    }

    partial void OnSelectedLayerChanged(TileLayerConfig? value)
    {
        if (value != null) tileLayerService.SelectedLayer = value;
    }

    [RelayCommand]
    private async Task AddCustomLayer()
    {
        var config = await dialogService.ShowAddTileLayerDialogAsync();
        if (config != null)
        {
            await tileLayerService.AddCustomLayerAsync(config);
            SelectedLayer = config;
        }
    }
}
