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
using Sufni.Telemetry;

namespace Sufni.App.Tests.Segments.Views;

[Collection("Ui")]
public class SegmentPickerPanelViewTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task TerrainLabels_ShowOneRowPerLabelOrTheEmptyMessage(int count)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureFluentTheme();
        using var labels = new SourceCache<TerrainLabelSnapshot, Guid>(label => label.Id);
        labels.AddOrUpdate(Enumerable.Range(0, count)
            .Select(i => new TerrainLabelSnapshot(Guid.NewGuid(), "source-hash", i * 2, i * 2 + 1, RoughnessClass.Rough)));
        var labelStore = Substitute.For<ITerrainLabelStore>();
        labelStore.Connect().Returns(labels.Connect());
        using var picker = TestSegmentPickers.CreateFactory(labelStore: labelStore).Create(Guid.NewGuid(),
            Observable.Return(new SegmentPickerSessionState(1000, 10, null, "source-hash")), _ => { }, _ => { }, _ => { });
        var view = new SegmentPickerPanelView { DataContext = picker };
        var host = await ViewTestHelpers.ShowViewAsync(view);
        try
        {
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Equal(count == 0, view.FindControl<TextBlock>("EmptyTerrainLabelsMessage")!.IsVisible);
            Assert.Equal(count, view.FindAllVisual<Button>().Count(button => button.Name == "TerrainLabelRowButton"));
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}
