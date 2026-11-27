using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Sessions;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Detail.Views.Editors;

[Collection("Ui")]
public class SessionDetailViewTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SessionDetailView_ShowsSensorFindingsPanel_OnlyWhenTheLoadedGenerationHasFindings(bool hasFindings)
    {
        var context = new SessionDetailViewTestContext();
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        SensorFinding[] findings = hasFindings ? [new(new SensorIdentity(SensorKind.ShockTravel), SensorFaultReason.Frozen, [new(1, 3)])] : [];

        await using var mounted = await context.MountMobileAsync(snapshot,
            context.CreateLoadedState(TestTelemetryData.CreateProcessed() with { SensorFindings = [.. findings] }),
            SessionDetailHarness.DomainFromSnapshot(snapshot));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(hasFindings, mounted.View.FindNamedVisual<Panel>("SensorFindingsPanel")!.IsVisible);
    }
}
