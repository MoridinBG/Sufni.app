using Avalonia.Headless.XUnit;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Segments.Views;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Shared.Views.Controls;
using static Sufni.App.Tests.TestSupport.Fixtures.TestSegmentComparisonRows;

namespace Sufni.App.Tests.Segments.Views;

[Collection("Ui")]
public class SegmentComparisonRowsTests
{
    private readonly SignalRow parent = new() { RowId = "travel" };
    private readonly SignalRow declared = new() { RowId = "velocity" };

    public SegmentComparisonRowsTests()
    {
        parent.ChildRows.Add(declared);
    }

    [AvaloniaFact]
    public void Rows_AreCollapsedChildRowsBeforeTheDeclaredChildren()
    {
        var first = TravelRow();
        var second = TravelRow();

        SegmentComparisonRows.SetRows(parent, [first, second]);

        Assert.Equal([first.Title, second.Title, null], parent.ChildRows.Select(row => row.Title));
        Assert.All(parent.ChildRows.Take(2), row =>
        {
            Assert.False(row.IsExpanded);
            Assert.Null(row.RowId);
        });
    }

    [AvaloniaFact]
    public void Rows_KeepTheRowOfAComparisonThatStays()
    {
        var kept = TravelRow();
        SegmentComparisonRows.SetRows(parent, [kept, TravelRow()]);
        var keptRow = parent.ChildRows[0];

        SegmentComparisonRows.SetRows(parent, [kept]);

        Assert.Equal([keptRow, declared], parent.ChildRows);
    }

    [AvaloniaFact]
    public void Timeline_ReachesTheComparisonPlots()
    {
        var timeline = new SessionTimelineLinkViewModel();
        SegmentComparisonRows.SetRows(parent, [TravelRow()]);

        SegmentComparisonRows.SetTimeline(parent, timeline);

        Assert.Same(timeline, Assert.IsType<SegmentComparisonPlotView>(parent.ChildRows[0].PlotContent).Timeline);
    }

    private static SegmentComparisonRowViewModel TravelRow() =>
        Create(SegmentComparisonSignal.Travel, [Series(SegmentComparisonSeriesRole.FrontTravel, 1, 2, 3)]);
}
