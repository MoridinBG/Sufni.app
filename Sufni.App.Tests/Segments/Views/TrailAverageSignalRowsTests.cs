using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Segments.Views;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Signals.Views.Controls;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Sessions;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Segments.Views;

[Collection("Ui")]
public class TrailAverageSignalRowsTests
{
    [AvaloniaFact]
    public async Task RecordedSignalsHostsAnAverageOnBothTerrainRows()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var telemetry = TestSegmentMatches.Telemetry();
        TestSegmentComparisons.WithTrailAverage(h.SegmentCoordinator);
        var editor = await h.LoadMatchedEditorAsync(session, matches:
            [SegmentComparisonBuilder.Build(TestSegmentMatches.Match(10, 13), telemetry, 30)!,
                SegmentComparisonBuilder.Build(TestSegmentMatches.Match(6, 12), telemetry, 30)!]);
        try
        {
            var signals = editor.SignalsWorkspace;
            ViewTestHelpers.EnsureFluentTheme();
            ViewTestHelpers.EnsureViewTestResources();
            var view = new RecordedSignalRowsView { DataContext = signals };
            await using var mounted = await ViewTestHelpers.MountAsync(view);
            await ViewTestHelpers.FlushDispatcherAsync();

            var root = view.FindControl<SignalRowsRoot>("SignalRowsRoot")!;
            var terrain = root.Rows.Single(r => r.RowId == SignalRowIds.Terrain);
            foreach (var parent in terrain.ChildRows.Where(row => row.RowId == SignalRowIds.Roughness || row.RowId == SignalRowIds.Grade))
            {
                var child = Assert.Single(parent.ChildRows);
                var plot = Assert.IsType<SegmentComparisonPlotView>(child.PlotContent);
                Assert.Equal(3, Assert.IsType<TrailAverageRow>(plot.DataContext).RunCount);
                Assert.Same(signals.Timeline, plot.Timeline);
            }
            Assert.Equal(2, terrain.ChildRows.Count(r => r.ChildRows.Count == 1));
        }
        finally
        {
            await h.CloseAsync(editor);
        }
    }
}
