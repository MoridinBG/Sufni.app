using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DynamicData;
using NSubstitute;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Segments.Views;
using Sufni.App.Terrain.Stores;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Segments.Views;

[Collection("Ui")]
public class SegmentPickerPanelViewTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task TerrainLabels_ShowOneRowPerLabelOrTheEmptyMessage(int count) =>
        await WithViewAsync(Enumerable.Range(0, count).Select(i => Label(i * 2, new TerrainLabelClass.Roughness(RoughnessClass.Rough))),
            (view, _) =>
            {
                Assert.Equal(count == 0, view.FindControl<TextBlock>("EmptyTerrainLabelsMessage")!.IsVisible);
                Assert.Equal(count, view.FindAllVisual<Button>().Count(button => button.Name == "TerrainLabelRowButton"));
            });

    [AvaloniaFact]
    public async Task SteepnessLabelRowsRunThePickerLabelCommands() =>
        await WithViewAsync([Label(0, new TerrainLabelClass.Steepness(GradeBand.SteepDescent))], (view, picker) =>
        {
            var buttons = view.FindControl<ItemsControl>("SteepnessLabelsItems")!.FindAllVisual<Button>().ToDictionary(button => button.Name!);

            Assert.Equal([picker.SelectTerrainLabelCommand, picker.ChangeTerrainLabelClassCommand, picker.DeleteTerrainLabelCommand],
                [buttons["TerrainLabelRowButton"].Command, buttons["ChangeTerrainLabelClassButton"].Command, buttons["DeleteTerrainLabelButton"].Command]);
        });

    private static TerrainLabelSnapshot Label(double start, TerrainLabelClass labelClass) =>
        new(Guid.NewGuid(), "source-hash", start, start + 1, labelClass);

    private static async Task WithViewAsync(IEnumerable<TerrainLabelSnapshot> snapshots, Action<SegmentPickerPanelView, SegmentPickerViewModel> assert)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureFluentTheme();
        using var labels = new SourceCache<TerrainLabelSnapshot, Guid>(label => label.Id);
        labels.AddOrUpdate(snapshots);
        var labelStore = Substitute.For<ITerrainLabelStore>();
        labelStore.Connect().Returns(labels.Connect());
        using var picker = TestSegmentPickers.CreateFactory(labelStore: labelStore).Create(Guid.NewGuid(),
            Observable.Return(new SegmentPickerSessionState(1000, 10, null, "source-hash")), _ => { }, _ => { }, _ => { });
        var view = new SegmentPickerPanelView { DataContext = picker };
        var host = await ViewTestHelpers.ShowViewAsync(view);
        try
        {
            await ViewTestHelpers.FlushDispatcherAsync();
            assert(view, picker);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}
