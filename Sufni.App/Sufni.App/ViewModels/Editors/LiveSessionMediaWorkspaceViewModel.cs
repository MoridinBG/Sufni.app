using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.ViewModels;

namespace Sufni.App.ViewModels.Editors;

public sealed class LiveSessionMediaWorkspaceViewModel : ViewModelBase, ISessionMediaWorkspace
{
    private bool isInitialized;

    public MapViewModel? MapViewModel { get; }
    public bool HasSessionTrackPoints => MapViewModel?.SessionTrackPoints?.Count > 0;
    public SessionTimelineLinkViewModel Timeline { get; }
    public double? MapVideoWidth { get; } = 400;
    public string? VideoUrl => null;

    public LiveSessionMediaWorkspaceViewModel()
        : this(tileLayerService: null, dialogService: null, new SessionTimelineLinkViewModel())
    {
    }

    public LiveSessionMediaWorkspaceViewModel(
        ITileLayerService? tileLayerService,
        IDialogService? dialogService,
        SessionTimelineLinkViewModel timeline)
    {
        Timeline = timeline;

        if (tileLayerService is null || dialogService is null)
        {
            return;
        }

        MapViewModel = new MapViewModel(tileLayerService, dialogService)
        {
            FullTrackPoints = [],
            SessionTrackPoints = [],
        };
    }

    public async Task InitializeAsync()
    {
        if (MapViewModel is null || isInitialized)
        {
            return;
        }

        isInitialized = true;
        await MapViewModel.InitializeAsync();
    }

    public void SetTrackPoints(IReadOnlyList<TrackPoint>? trackPoints)
    {
        if (MapViewModel is null)
        {
            return;
        }

        MapViewModel.SessionTrackPoints = trackPoints?.ToList() ?? [];
        OnPropertyChanged(nameof(HasSessionTrackPoints));
    }
}