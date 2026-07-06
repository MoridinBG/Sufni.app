using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.Extensibility.Views;
using Sufni.App.Sessions.Signals.DesktopViews.Items;
using Sufni.App.Sessions.Signals.Views.Controls;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Sessions;

namespace Sufni.App.Tests.Sessions.Signals.DesktopViews.Items;

[Collection("Ui")]
public class RecordedSessionSignalsDesktopViewTests
{
    [AvaloniaFact]
    public async Task RecordedSessionSignalsDesktopView_ComposesToolbarAndSignalRowsHosts()
    {
        var workspace = new TestRecordedSessionSignalsWorkspace(TestTelemetryData.CreateMinimal());
        workspace.ExtensionSlots.SignalToolbarViews.Add(new RecordedSessionToolbarViewContribution(
            "extension",
            "toolbar-view",
            Order: 0,
            RecordedSessionToolbarZone.Trailing,
            new TestContributionViewModel
            {
                Content = new TextBlock { Name = "DesktopSignalToolbarContribution", Text = "Toolbar" },
            }));

        await using var mounted = await MountAsync(workspace);

        Assert.Single(mounted.View.GetVisualDescendants().OfType<RecordedSessionToolbarContributionsView>());
        Assert.Single(mounted.View.GetVisualDescendants().OfType<RecordedSignalRowsView>());
        Assert.Single(mounted.View.GetVisualDescendants().OfType<SignalRowsRoot>());
    }

    private static async Task<MountedRecordedSessionSignalsDesktopView> MountAsync(TestRecordedSessionSignalsWorkspace workspace)
    {
        ViewTestHelpers.EnsureSessionDetailViewSetup(isDesktop: true);

        var view = new RecordedSessionSignalsDesktopView
        {
            DataContext = workspace,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedRecordedSessionSignalsDesktopView(host, view);
    }

}

internal sealed class MountedRecordedSessionSignalsDesktopView(Window host, RecordedSessionSignalsDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public RecordedSessionSignalsDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
