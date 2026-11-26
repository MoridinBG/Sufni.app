using DynamicData;
using System.Reactive.Linq;
using NSubstitute;

using Sufni.App.Infrastructure;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.Stores;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Terrain.Coordinators;
using Sufni.App.Terrain.Stores;
namespace Sufni.App.Tests.TestSupport.Doubles;

internal static class TestSegmentPickers
{
    public static ISegmentPickerFactory CreateFactory(
        ISessionSegmentStore? segmentStore = null,
        ISegmentCoordinator? segmentCoordinator = null,
        IDialogService? dialogService = null,
        ITerrainLabelStore? labelStore = null,
        ITerrainLabelCoordinator? labelCoordinator = null) =>
        new SegmentPickerFactory(
            segmentStore ?? EmptyStore(),
            segmentCoordinator ?? Substitute.For<ISegmentCoordinator>(),
            labelStore ?? EmptyLabelStore(),
            labelCoordinator ?? Substitute.For<ITerrainLabelCoordinator>(),
            dialogService ?? Substitute.For<IDialogService>());

    public static SegmentPickerViewModel Create() =>
        CreateFactory().Create(Guid.NewGuid(), Observable.Never<SegmentPickerSessionState>(), _ => { }, _ => { }, _ => { });
    private static ISessionSegmentStore EmptyStore()
    {
        var store = Substitute.For<ISessionSegmentStore>();
        store.Connect().Returns(Observable.Empty<IChangeSet<SessionSegmentSnapshot, Guid>>());
        store.GetForOwner(Arg.Any<string>()).Returns(Array.Empty<SessionSegmentSnapshot>());
        return store;
    }

    private static ITerrainLabelStore EmptyLabelStore()
    {
        var store = Substitute.For<ITerrainLabelStore>();
        store.Connect().Returns(Observable.Empty<IChangeSet<TerrainLabelSnapshot, Guid>>());
        return store;
    }
}
