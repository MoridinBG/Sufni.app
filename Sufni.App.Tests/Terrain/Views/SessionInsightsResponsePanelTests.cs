using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.Sessions.Insights.Services.SessionInsights;
using Sufni.App.Sessions.Insights.Views.Controls;
using Sufni.App.Sessions.Models;
using Sufni.App.Terrain.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Terrain.Views;

[Collection("Ui")]
public class SessionInsightsResponsePanelTests
{
    [AvaloniaFact]
    public async Task InsufficientCoverageShowsTheCountsAndAFittedBaselineShowsFourClassRows()
    {
        var profiles = SyntheticResponseRides.Baseline;
        var service = new SessionInsightsService();
        var request = AnalysisTelemetryFixture.CreateRequest(TestTelemetryData.CreateProcessed());
        SessionResponsePanel Panel(TerrainBaseline baseline) => service.Analyze(request with
            { Terrain = new(profiles[5], baseline, null) }).Response;
        ViewTestHelpers.EnsureFluentTheme();
        ViewTestHelpers.EnsureViewTestResources();
        var view = new SessionInsightsResponsePanel { DataContext = service.Analyze(request).Response };
        await using var mounted = await ViewTestHelpers.MountAsync(view);
        Assert.False(view.IsVisible);

        var limited = Panel(new TerrainBaseline.Insufficient(TerrainBaselineIdentity.Empty,
            SuspensionResponseModel.Fit(profiles[5], profiles[..4])));
        view.DataContext = limited;
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(view.IsVisible);
        Assert.Equal(4, limited.BaselineSessions);
        Assert.True(limited.BaselineWindows > 0);
        Assert.Empty(view.GetVisualDescendants().OfType<Control>().Where(control => control.DataContext is SessionResponseRow));

        var fitted = Panel(new TerrainBaseline.Fitted(TerrainBaselineIdentity.Empty,
            SuspensionResponseModel.Fit(profiles[5], profiles[..5])));
        view.DataContext = fitted;
        await ViewTestHelpers.FlushDispatcherAsync();
        var rows = view.GetVisualDescendants().OfType<StackPanel>().Select(p => p.DataContext)
            .OfType<SessionResponseRow>().Distinct().ToList();
        Assert.Equal(4, rows.Count);
        Assert.Contains(rows, r => r.Measurement.Class is null);
        Assert.Equal(Enum.GetValues<RoughnessClass>(), rows.Where(r => r.Measurement.Class.HasValue).Select(r => r.Measurement.Class!.Value));
    }
}
