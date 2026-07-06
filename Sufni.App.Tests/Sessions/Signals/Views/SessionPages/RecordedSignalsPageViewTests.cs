using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.Extensibility.Views;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Signals.ViewModels.SessionPages;
using Sufni.App.Sessions.Signals.Views.Controls;
using Sufni.App.Sessions.Signals.Views.SessionPages;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Sessions;

namespace Sufni.App.Tests.Sessions.Signals.Views.SessionPages;

[Collection("Ui")]
public class RecordedSignalsPageViewTests
{
    [AvaloniaFact]
    public async Task RecordedSignalsPageView_ComposesToolbarAndSignalRows()
    {
        var signalsWorkspace = new TestRecordedSessionSignalsWorkspace(TestTelemetryData.CreateMinimal());
        signalsWorkspace.ExtensionSlots.SignalToolbarViews.Add(new RecordedSessionToolbarViewContribution(
            "extension",
            "toolbar-view",
            Order: 0,
            RecordedSessionToolbarZone.Trailing,
            new TestContributionViewModel
            {
                Content = new TextBlock { Name = "MobileSignalToolbarContribution", Text = "Toolbar" },
            }));
        var mediaWorkspace = new TestSessionMediaWorkspace(
            trackPoints:
            [
                new TrackPoint(0, 0, 0, null),
                new TrackPoint(1, 100, 100, null),
            ]);
        var page = new RecordedSignalsPageViewModel(signalsWorkspace, mediaWorkspace);

        await using var mounted = await MountAsync(page);

        Assert.Single(mounted.View.GetVisualDescendants().OfType<RecordedSessionToolbarContributionsView>());
        Assert.Single(mounted.View.GetVisualDescendants().OfType<RecordedSignalRowsView>());
        Assert.Single(mounted.View.GetVisualDescendants().OfType<SignalRowsRoot>());
    }

    private static async Task<MountedRecordedSignalsPageView> MountAsync(RecordedSignalsPageViewModel page)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsurePlotViewStyle();

        var view = new RecordedSignalsPageView
        {
            DataContext = page,
        };

        var host = await ViewTestHelpers.ShowViewAsync(new ScrollViewer { Content = view });
        return new MountedRecordedSignalsPageView(host, view);
    }

}

internal sealed record MountedRecordedSignalsPageView(Window Host, RecordedSignalsPageView View) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
