using System.Reactive.Subjects;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Shared.Models;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Plots;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

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
        var added = new TileLayerConfig { Name = "Added", UrlTemplate = "https://example/{z}/{x}/{y}" };
        using var state = new BehaviorSubject<TileLayerState>(new([original], original));
        var service = Substitute.For<ITileLayerService>();
        service.Observe().Returns(state);
        var coordinator = Substitute.For<IMapLayerCoordinator>();
        coordinator.AddAndSelectAsync(Arg.Any<TileLayerConfig>()).Returns(new MapLayerWriteResult.Saved());
        var dialog = Substitute.For<IDialogService>();
        MapLayerFixture.ConfirmAddDialog(dialog, added);
        using var vm = new MapViewModel(service, coordinator, dialog, new MapGeometryFactory(new InlineBackgroundTaskRunner()), new InlineUiThreadDispatcher(), new FakeTimeProvider());

        await vm.AddCustomLayerCommand.ExecuteAsync(null);
        Assert.Equal([original], vm.AvailableLayers);
        Assert.Same(original, vm.SelectedLayer);
        await coordinator.Received(1).AddAndSelectAsync(Arg.Is<TileLayerConfig>(value => value.Name == added.Name && value.UrlTemplate == added.UrlTemplate));

        state.OnNext(new([original, added], added));
        Assert.Equal([original, added], vm.AvailableLayers);
        Assert.Same(added, vm.SelectedLayer);

        vm.Dispose();
        state.OnNext(new([original], original));
        Assert.Same(added, vm.SelectedLayer);
    }

    private static readonly TerrainPresentation RoughThenSmooth = RecordedSessionPresentationDeriver.CreateTerrain(TerrainClassifier.Classify(
        TerrainProfiles.Classified(TerrainProfiles.Window(0, RoughnessClass.Rough, RoughnessSource.Both),
            TerrainProfiles.Window(1, RoughnessClass.Smooth, RoughnessSource.Both))));

    [Fact]
    public void LegendEntryHidesAndShowsItsClassOnTheTrack()
    {
        using var vm = TerrainMap();
        var rough = SufniThemes.SignalSeries.Terrain.ColorFor(RoughnessClass.Rough);
        var smooth = SufniThemes.SignalSeries.Terrain.ColorFor(RoughnessClass.Smooth);

        vm.TerrainLegend.Single(entry => entry.Colour == rough).Toggle.Execute(null);
        Assert.False(vm.TerrainLegend.Single(entry => entry.Colour == rough).IsShown);
        Assert.Equal(new Avalonia.Media.Color?[] { null, smooth }, vm.TerrainRuns.Select(run => run.Colour));

        vm.TerrainLegend.Single(entry => entry.Colour == rough).Toggle.Execute(null);
        Assert.Equal(new Avalonia.Media.Color?[] { rough, smooth }, vm.TerrainRuns.Select(run => run.Colour));
    }

    [Fact]
    public void HiddenGroundWithoutAClassBelongsToItsColouring()
    {
        using var vm = TerrainMap();

        vm.TerrainLegend[^1].Toggle.Execute(null);
        Assert.True(vm.UnclassifiedHidden);
        vm.ApplyTerrain(MapTrackColoring.Grade, RoughThenSmooth);
        Assert.False(vm.UnclassifiedHidden);
        vm.ApplyTerrain(MapTrackColoring.Roughness, RoughThenSmooth);
        Assert.True(vm.UnclassifiedHidden);
    }

    private static MapViewModel TerrainMap()
    {
        var vm = new MapViewModel(Substitute.For<ITileLayerService>(), Substitute.For<IMapLayerCoordinator>(),
            Substitute.For<IDialogService>(), new MapGeometryFactory(new InlineBackgroundTaskRunner()),
            new InlineUiThreadDispatcher(), new FakeTimeProvider());
        vm.ApplyTerrain(MapTrackColoring.Roughness, RoughThenSmooth);
        return vm;
    }
}
