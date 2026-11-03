using DynamicData;
using System.Reactive.Linq;
using NSubstitute;

using Sufni.App.Infrastructure;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.Stores;
using Sufni.App.Segments.ViewModels;
namespace Sufni.App.Tests.TestSupport.Doubles;

internal static class TestSegmentPickers
{
    public static ISegmentPickerFactory CreateFactory(
        ISessionSegmentStore? segmentStore = null,
        ISegmentCoordinator? segmentCoordinator = null,
        IDialogService? dialogService = null) =>
        new SegmentPickerFactory(
            segmentStore ?? EmptyStore(),
            segmentCoordinator ?? Substitute.For<ISegmentCoordinator>(),
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
}
