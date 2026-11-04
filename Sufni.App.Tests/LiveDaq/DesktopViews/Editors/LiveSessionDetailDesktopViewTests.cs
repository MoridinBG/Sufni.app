using System.Reactive.Subjects;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Coordinators.Capture;
using Sufni.App.LiveDaq.DesktopViews.Editors;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.LiveDaq.Views.Editors;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Detail.DesktopViews.Items;
using Sufni.App.Sessions.Services;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Extensions;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.Telemetry;

namespace Sufni.App.Tests.LiveDaq.DesktopViews.Editors;

[Collection("Ui")]
public class LiveSessionDetailDesktopViewTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureState_UpdatesControlsThroughTheComposedWorkspace(bool mobile)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(mobile ? UiLayoutProfile.Compact : UiLayoutProfile.Workspace);
        using var states = new BehaviorSubject<LiveCaptureState>(LiveCaptureState.Empty);
        using var batches = new Subject<LiveSignalBatch>();
        using var bikes = new BikeStoreStub();
        var capture = Substitute.For<ILiveCaptureCoordinator>();
        capture.States.Returns(states);
        capture.SignalBatches.Returns(batches);
        capture.Current.Returns(_ => states.Value);
        capture.DisposeAsync().Returns(ValueTask.CompletedTask);
        var context = new LiveDaqSessionContext("board", Guid.NewGuid(), "Board", Guid.NewGuid(), "Setup",
            Guid.NewGuid(), "Bike", new BikeData(180, 170, count => count, count => count),
            new LiveDaqTravelCalibration(null, null));
        var editor = new LiveSessionDetailViewModel(context, capture, TestCoordinatorSubstitutes.LiveDaq(),
            Substitute.For<ISessionPresentationService>(), new InlineBackgroundTaskRunner(),
            new TestMapViewModelFactory(Substitute.For<ITileLayerService>().WithDefaultLayerState()),
            Substitute.For<IShellCoordinator>(), Substitute.For<IDialogService>(), new InlineUiThreadDispatcher(),
            new FakeTimeProvider(), bikes, TestCoordinatorSubstitutes.Bike());
        editor.SetTabActive(true);
        UserControl view = mobile ? new LiveSessionDetailView() : new LiveSessionDetailDesktopView();
        view.DataContext = editor;
        await using var mounted = new MountedView<UserControl>(await ViewTestHelpers.ShowViewAsync(view), view);
        try
        {
            await editor.LoadedCommand.ExecuteAsync(null);
            var duration = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), x => x.Name == "LiveCaptureDurationTextBlock");
            var error = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), x => x.Name == "LiveLastErrorTextBlock");
            var emptyDuration = duration.Text;
            Assert.False(error.IsVisible);

            states.OnNext(LiveCaptureState.Empty with
            {
                Revision = 1,
                Controls = LiveCaptureControls.Empty with
                {
                    CaptureDuration = TimeSpan.FromSeconds(7), Phase = LiveCapturePhase.AwaitingReset,
                },
            });
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.NotEqual(emptyDuration, duration.Text);
            Assert.True(error.IsVisible);
            if (!mobile)
            {
                var sidebar = Assert.Single(view.GetVisualDescendants().OfType<SessionSidebarDesktopView>());
                sidebar.FindControl<TextBox>("NameTextBox")!.Text = "Evening run";
                Assert.Equal("Evening run", editor.Name);
            }
        }
        finally { await editor.PrepareCloseAsync(); }
    }
}
