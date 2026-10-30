using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Segments.Views;
using Sufni.App.Shared.Models;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Segments.Views;

[Collection("Ui")]
public class StravaFailureViewsTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedRequest_RendersDiagnosticsInCollapsedExpander(bool connecting)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureFluentTheme();
        const string details = "technical diagnostic";
        var coordinator = Substitute.For<ISegmentCoordinator>();
        coordinator.ConnectStravaAsync(Arg.Any<StravaConnectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StravaConnectResult.Failed(StravaConnectFailureReason.Connect, details));
        coordinator.FindStravaSegmentsAsync(Arg.Any<long?>(), Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(new StravaSegmentFetchResult.Failed(StravaSegmentFetchFailureReason.Load, details));
        using var picker = TestSegmentPickers.CreateFactory(segmentCoordinator: coordinator).Create(
            Guid.NewGuid(), Observable.Return(new SegmentPickerSessionState(1000, 10, null)), _ => { }, _ => { }, _ => { });
        var dialog = new StravaConnectDialogViewModel(coordinator, new StravaConnectionSnapshot("client", "secret", false))
        {
            RedirectUrl = "http://localhost/exchange_token?code=abc"
        };
        dialog.MarkAuthorizationUrlOpened();
        Control view = connecting
            ? new StravaConnectDialogView { DataContext = dialog }
            : new SegmentPickerPanelView { DataContext = picker };
        var host = await ViewTestHelpers.ShowViewAsync(view);
        try
        {
            if (connecting) await dialog.ConnectCommand.ExecuteAsync(null);
            else await picker.OpenCommand.ExecuteAsync(null);
            await ViewTestHelpers.FlushDispatcherAsync();

            var expander = view.FindControl<Expander>("StravaErrorDetailsExpander")!;
            Assert.True(expander.IsVisible);
            Assert.False(expander.IsExpanded);
            expander.IsExpanded = true;
            await ViewTestHelpers.FlushDispatcherAsync();
            Assert.Equal(details, Assert.IsType<SelectableTextBlock>(expander.Content).Text);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}
