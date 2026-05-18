using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Sufni.App.Presentation;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views.Controls;
using Sufni.App.Views.Plots;

namespace Sufni.App.Tests.Views.Controls;

public class TelemetryPlotRowTests
{
    [AvaloniaFact]
    public async Task TelemetryPlotRow_CollapsesAndExpands_FromHeaderClick()
    {
        var row = CreateRow("Travel");
        await using var mounted = await MountAsync(row);

        Measure(row, 400, row.GetPreferredGroupHeight());
        Assert.True(row.IsExpanded);
        Assert.Equal(212, row.AllocatedGroupHeight);

        var header = Assert.Single(row.GetVisualDescendants().OfType<Button>());
        await ClickHeaderAsync(mounted, header);
        Measure(row, 400, row.GetPreferredGroupHeight());

        Assert.False(row.IsExpanded);
        Assert.Equal(row.CollapsedHeaderHeight, row.AllocatedGroupHeight);

        await ClickHeaderAsync(mounted, header);
        Measure(row, 400, row.GetPreferredGroupHeight());

        Assert.True(row.IsExpanded);
        Assert.Equal(212, row.AllocatedGroupHeight);
    }

    [AvaloniaFact]
    public async Task TelemetryPlotRow_HiddenPresentation_RemovesRowFromLayout()
    {
        var row = CreateRow("Velocity");
        row.PresentationState = SurfacePresentationState.Hidden;

        await using var mounted = await MountAsync(row);
        Measure(row, 400, 260);

        Assert.False(row.IsVisible);
        Assert.Equal(0, row.AllocatedGroupHeight);
    }

    [AvaloniaFact]
    public async Task TelemetryPlotRow_HeaderSpansRowAndUsesPlusMinusGlyphs()
    {
        var row = CreateRow("Travel");
        await using var mounted = await MountAsync(row);
        Measure(row, 400, row.GetPreferredGroupHeight());

        var header = Assert.Single(row.GetVisualDescendants().OfType<Button>());
        var glyph = row.GetVisualDescendants().OfType<TextBlock>().First();
        Assert.Equal(400, header.Bounds.Width);
        Assert.Equal("-", glyph.Text);

        await ClickHeaderAsync(mounted, header, new Point(390, 16));
        Measure(row, 400, row.GetPreferredGroupHeight());

        Assert.Equal(400, header.Bounds.Width);
        Assert.Equal("+", glyph.Text);
    }

    [AvaloniaFact]
    public async Task TelemetryPlotRow_HeaderClick_ToleratesSmallPointerMovement()
    {
        var row = CreateRow("Travel");
        await using var mounted = await MountAsync(row);
        Measure(row, 400, row.GetPreferredGroupHeight());

        var header = Assert.Single(row.GetVisualDescendants().OfType<Button>());

        await ClickHeaderAsync(mounted, header, new Point(200, 16), new Point(204, 18));
        Measure(row, 400, row.GetPreferredGroupHeight());

        Assert.False(row.IsExpanded);
    }

    [AvaloniaFact]
    public async Task TelemetryPlotRow_NestedPlot_UsesHostPlotHeight()
    {
        var velocity = CreateRow("Velocity");
        var travel = CreateRow("Travel");
        travel.ChildRows.Add(velocity);

        await using var mounted = await MountAsync(travel);
        Measure(travel, 400, travel.GetPreferredGroupHeight());

        Assert.Equal(travel.AllocatedPlotHeight, velocity.AllocatedPlotHeight);
        Assert.Equal(180, travel.AllocatedPlotHeight);
        Assert.Equal(424, travel.AllocatedGroupHeight);
    }

    [AvaloniaFact]
    public async Task TelemetryPlotRow_AppliesPlotBackgrounds_ToHostedPlotView()
    {
        var plotView = new TestPlotView();
        var row = CreateRow("Travel");
        row.PlotContent = plotView;
        row.PlotFigureBackground = Color.Parse("#101820");
        row.PlotDataBackground = Color.Parse("#203040");

        await using var mounted = await MountAsync(row);
        Measure(row, 400, row.GetPreferredGroupHeight());

        Assert.Equal(Color.Parse("#101820"), plotView.PlotFigureBackground);
        Assert.Equal(Color.Parse("#203040"), plotView.PlotDataBackground);
    }

    [AvaloniaFact]
    public async Task TelemetryPlotRow_DefaultsNestedPlotBackgrounds()
    {
        var childPlotView = new TestPlotView();
        var velocity = CreateRow("Velocity");
        velocity.PlotContent = childPlotView;
        var travel = CreateRow("Travel");
        travel.ChildRows.Add(velocity);

        await using var mounted = await MountAsync(travel);
        Measure(travel, 400, travel.GetPreferredGroupHeight());

        Assert.Equal(16, velocity.TitleLeftInset);
        AssertSolidBrush(Color.Parse("#0F1314"), velocity.RowBackground);
        AssertSolidBrush(Color.Parse("#101416"), velocity.HeaderBackground);
        Assert.Equal(Color.Parse("#101518"), velocity.PlotFigureBackground);
        Assert.Equal(Color.Parse("#1A2024"), velocity.PlotDataBackground);
        Assert.Equal(Color.Parse("#101518"), childPlotView.PlotFigureBackground);
        Assert.Equal(Color.Parse("#1A2024"), childPlotView.PlotDataBackground);

        var childBorder = Assert.IsType<Border>(velocity.Content);
        Assert.Equal(0, childBorder.Margin.Left);
        var hostedGlyph = velocity.GetVisualDescendants().OfType<TextBlock>().First();
        Assert.Equal(16, hostedGlyph.Margin.Left);
    }

    [AvaloniaFact]
    public async Task TelemetryPlotRow_NestedHostedRows_UseProgressiveTitleInsetsAndParentConnectors()
    {
        var acceleration = CreateRow("Acceleration");
        var velocity = CreateRow("Velocity");
        velocity.ChildRows.Add(acceleration);
        var travel = CreateRow("Travel");
        travel.ChildRows.Add(velocity);

        await using var mounted = await MountAsync(travel);
        Measure(travel, 400, travel.GetPreferredGroupHeight());

        Assert.Equal(16, velocity.TitleLeftInset);
        Assert.Equal(32, acceleration.TitleLeftInset);
        Assert.True(travel.HasVisibleChildConnectors);
        Assert.True(velocity.HasVisibleChildConnectors);
        Assert.False(acceleration.HasVisibleChildConnectors);
        Assert.Equal(2, travel.ChildConnectorSegmentCount);
        Assert.Equal(2, velocity.ChildConnectorSegmentCount);
        Assert.True(velocity.ChildConnectorStartLeft > travel.ChildConnectorStartLeft);

        var velocityBorder = Assert.IsType<Border>(velocity.Content);
        var accelerationBorder = Assert.IsType<Border>(acceleration.Content);
        Assert.Equal(0, velocityBorder.Margin.Left);
        Assert.Equal(0, accelerationBorder.Margin.Left);

        travel.IsExpanded = false;
        Measure(travel, 400, travel.GetPreferredGroupHeight());

        Assert.False(travel.HasVisibleChildConnectors);
    }

    [AvaloniaFact]
    public void TelemetryPlotRow_DefaultMinimumPlotHeight_KeepsManualResizeReadable()
    {
        var row = new TelemetryPlotRow
        {
            Title = "Travel",
            PresentationState = SurfacePresentationState.Ready,
            PlotContent = new Border(),
            HeaderHeight = 32,
            CollapsedHeaderHeight = 32,
        };

        Assert.Equal(160, row.MinimumPlotHeight);
        Assert.Equal(192, row.GetMinimumGroupHeight());
    }

    private static TelemetryPlotRow CreateRow(string title)
    {
        return new TelemetryPlotRow
        {
            Title = title,
            PresentationState = SurfacePresentationState.Ready,
            PlotContent = new Border(),
            PlaceholderContent = new Border(),
            PreferredPlotHeight = 180,
            MinimumPlotHeight = 96,
            HeaderHeight = 32,
            CollapsedHeaderHeight = 32,
            ChildRowGap = 4,
        };
    }

    private static async Task<MountedRow> MountAsync(TelemetryPlotRow row)
    {
        ViewTestHelpers.EnsureViewTestResources();
        var host = await ViewTestHelpers.ShowViewAsync(row);
        return new MountedRow(host);
    }

    private static void Measure(TelemetryPlotRow row, double width, double height)
    {
        row.ApplyAllocatedGroupHeight(height);
        row.Measure(new Size(width, row.AllocatedGroupHeight));
        row.Arrange(new Rect(0, 0, width, row.AllocatedGroupHeight));
    }

    private static async Task ClickHeaderAsync(
        MountedRow mounted,
        Button header,
        Point? relativeStart = null,
        Point? relativeEnd = null)
    {
        var start = header.TranslatePoint(relativeStart ?? new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), mounted.Host);
        var end = header.TranslatePoint(relativeEnd ?? relativeStart ?? new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), mounted.Host);
        Assert.NotNull(start);
        Assert.NotNull(end);

        mounted.Host.MouseDown(start.Value, MouseButton.Left, RawInputModifiers.None);
        mounted.Host.MouseUp(end.Value, MouseButton.Left, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();
    }

    private static void AssertSolidBrush(Color expectedColor, IBrush? brush)
    {
        var solidBrush = Assert.IsType<SolidColorBrush>(brush);
        Assert.Equal(expectedColor, solidBrush.Color);
    }

    private sealed class MountedRow : IAsyncDisposable
    {
        public MountedRow(Window host)
        {
            Host = host;
        }

        public Window Host { get; }

        public async ValueTask DisposeAsync()
        {
            Host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    private sealed class TestPlotView : SufniPlotView
    {
        protected override void CreatePlot()
        {
        }
    }
}
