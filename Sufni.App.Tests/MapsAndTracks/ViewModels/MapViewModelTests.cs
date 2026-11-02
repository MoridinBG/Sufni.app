using System.Reactive.Subjects;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Shared.Models;

namespace Sufni.App.Tests.MapsAndTracks.ViewModels;

public class MapViewModelTests
{
    [Fact]
    public async Task SelectLayerAsync_KeepsPublishedSelection_AndSeparatesFailureDetails()
    {
        var original = new TileLayerConfig { Name = "Original" };
        var other = new TileLayerConfig { Name = "Other" };
        using var state = new BehaviorSubject<TileLayerState>(new([original, other], original));
        var service = Substitute.For<ITileLayerService>();
        service.Observe().Returns(state);
        var coordinator = Substitute.For<IMapLayerCoordinator>();
        const string details = "disk diagnostic";
        coordinator.SelectAsync(other.Id).Returns(new MapLayerWriteResult.Failed(MapLayerFailureReason.Select, details));
        using var vm = new MapViewModel(service, coordinator, Substitute.For<IDialogService>(),
            new MapGeometryFactory(new InlineBackgroundTaskRunner()), new InlineUiThreadDispatcher(), new FakeTimeProvider());

        await vm.SelectLayerAsync(other);

        Assert.Same(original, vm.SelectedLayer);
        var failure = Assert.Single(vm.ErrorMessages);
        Assert.Equal(details, failure.Details);
        Assert.DoesNotContain(details, failure.Summary);
    }

    [Fact]
    public async Task AddCommand_ForwardsDraft_AndWaitsForPublishedState()
    {
        var original = new TileLayerConfig { Name = "Original" };
        var added = new TileLayerConfig { Name = "Added" };
        using var state = new BehaviorSubject<TileLayerState>(new([original], original));
        var service = Substitute.For<ITileLayerService>();
        service.Observe().Returns(state);
        var coordinator = Substitute.For<IMapLayerCoordinator>();
        coordinator.AddAndSelectAsync(added).Returns(new MapLayerWriteResult.Saved());
        var dialog = Substitute.For<IDialogService>();
        dialog.ShowAddTileLayerDialogAsync().Returns(added);
        using var vm = new MapViewModel(service, coordinator, dialog, new MapGeometryFactory(new InlineBackgroundTaskRunner()), new InlineUiThreadDispatcher(), new FakeTimeProvider());

        await vm.AddCustomLayerCommand.ExecuteAsync(null);
        Assert.Equal([original], vm.AvailableLayers);
        Assert.Same(original, vm.SelectedLayer);
        await coordinator.Received(1).AddAndSelectAsync(added);

        state.OnNext(new([original, added], added));
        Assert.Equal([original, added], vm.AvailableLayers);
        Assert.Same(added, vm.SelectedLayer);

        vm.Dispose();
        state.OnNext(new([original], original));
        Assert.Same(added, vm.SelectedLayer);
    }
}
