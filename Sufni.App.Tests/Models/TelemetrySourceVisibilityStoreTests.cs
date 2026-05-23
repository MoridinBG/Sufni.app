using Sufni.App.Models;

namespace Sufni.App.Tests.Models;

public class TelemetrySourceVisibilityStoreTests
{
    [Fact]
    public void IsVisible_ReturnsTrue_ForSourcesNotExplicitlyHidden()
    {
        var sut = new TelemetrySourceVisibilityStore();

        Assert.True(sut.IsVisible(TelemetryGraphRowIds.Travel, TelemetrySourceKeys.Front));
    }

    [Fact]
    public void SetVisible_StoresHiddenState_PerRowAndSource()
    {
        var sut = new TelemetrySourceVisibilityStore();

        sut.SetVisible(TelemetryGraphRowIds.Travel, TelemetrySourceKeys.Rear, visible: false);

        Assert.False(sut.IsVisible(TelemetryGraphRowIds.Travel, TelemetrySourceKeys.Rear));
        Assert.True(sut.IsVisible(TelemetryGraphRowIds.Travel, TelemetrySourceKeys.Front));
        Assert.True(sut.IsVisible(TelemetryGraphRowIds.Velocity, TelemetrySourceKeys.Rear));
    }

    [Fact]
    public void SetVisible_RestoresHiddenSource()
    {
        var sut = new TelemetrySourceVisibilityStore();
        sut.SetVisible(TelemetryGraphRowIds.Travel, TelemetrySourceKeys.Rear, visible: false);

        sut.SetVisible(TelemetryGraphRowIds.Travel, TelemetrySourceKeys.Rear, visible: true);

        Assert.True(sut.IsVisible(TelemetryGraphRowIds.Travel, TelemetrySourceKeys.Rear));
    }

    [Fact]
    public void Clear_RestoresAllHiddenSources()
    {
        var sut = new TelemetrySourceVisibilityStore();
        sut.SetVisible(TelemetryGraphRowIds.Travel, TelemetrySourceKeys.Rear, visible: false);
        sut.SetVisible(TelemetryGraphRowIds.Velocity, TelemetrySourceKeys.Front, visible: false);

        sut.Clear();

        Assert.True(sut.IsVisible(TelemetryGraphRowIds.Travel, TelemetrySourceKeys.Rear));
        Assert.True(sut.IsVisible(TelemetryGraphRowIds.Velocity, TelemetrySourceKeys.Front));
    }
}
