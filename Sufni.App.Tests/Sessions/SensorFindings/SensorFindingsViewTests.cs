using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.Sessions.SensorFindings.ViewModels;
using Sufni.App.Sessions.SensorFindings.Views;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Stores;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.SensorFindings;

[Collection("Ui")]
public class SensorFindingsViewTests
{
    private static readonly SensorIdentity Gps = new(SensorKind.Gps);

    [AvaloniaFact]
    public async Task SensorFindingsView_ShowsDropAndKeep_OnlyForDroppableRows()
    {
        using var publication = new StorePublication(new InlineUiThreadDispatcher());
        using var store = new SessionStore(publication);
        var session = TestSnapshots.Session();
        await store.PublishSessionsChangedAsync([session]);
        using var findings = new SensorFindingsViewModel(session.Id,
            [new(Gps, SensorFaultReason.Glitch, [new(5, 6)]), new(Gps, SensorFaultReason.FixLost, [new(50, 60)])],
            store, TestCoordinatorSubstitutes.Session(), new InlineUiThreadDispatcher(), _ => { }, _ => { });
        ViewTestHelpers.EnsureFluentTheme();
        ViewTestHelpers.EnsureViewTestResources();
        await using var mounted = await ViewTestHelpers.MountAsync(new SensorFindingsView { DataContext = findings });

        var dropButtons = mounted.View.GetVisualDescendants().OfType<Button>().Where(button => button.Name == "DropButton").ToArray();

        Assert.Equal([true, false], dropButtons.Select(button => button.IsEffectivelyVisible));
    }
}
