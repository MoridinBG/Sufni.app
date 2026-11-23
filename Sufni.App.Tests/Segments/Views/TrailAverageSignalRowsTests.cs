using System.Reactive.Linq;
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
        var profile = TerrainProfiles.Classified(TerrainProfiles.Window(0, RoughnessClass.Rough, RoughnessSource.Both));
        var editor = h.CreateRecordedEditor(session, h.CreateLoadedResult(telemetry,
            trackPoints: [new(1000, 0, 0, null), new(1030, 200, 50, null)]), Observable.Return(h.WithTerrain(session, profile)));
        try
        {
            await h.LoadAsync(editor);
            var signals = editor.SignalsWorkspace;
            signals.SetAnalysisRange(2, 5);
            TestSegmentComparisons.MatchReturns(h.SegmentCoordinator,
                SegmentComparisonBuilder.Build(TestSegmentMatches.Match(10, 13), telemetry, 30)!,
                SegmentComparisonBuilder.Build(TestSegmentMatches.Match(6, 12), telemetry, 30)!);
            TestSegmentComparisons.WithTrailAverage(h.SegmentCoordinator);
            ViewTestHelpers.EnsureFluentTheme();
            ViewTestHelpers.EnsureViewTestResources();
            var view = new RecordedSignalRowsView { DataContext = signals };
            await using var mounted = await ViewTestHelpers.MountAsync(view);
            await signals.SegmentComparison.MatchCommand.ExecuteAsync(null);
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
