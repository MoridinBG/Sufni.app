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
            segmentStore ?? new SessionSegmentStore(Substitute.For<ISessionSegmentRepository>(), new InlineUiThreadDispatcher()),
            segmentCoordinator ?? Substitute.For<ISegmentCoordinator>(),
            dialogService ?? Substitute.For<IDialogService>());

    public static SegmentPickerViewModel Create() =>
        CreateFactory().Create(Guid.NewGuid(), Observable.Never<SegmentPickerSessionState>(), _ => { }, _ => { });
}
