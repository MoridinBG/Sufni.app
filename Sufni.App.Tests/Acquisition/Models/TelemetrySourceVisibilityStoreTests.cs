
using Sufni.App.Acquisition.Models;
using Sufni.App.Infrastructure;
namespace Sufni.App.Tests.Acquisition.Models;

public class TelemetrySourceVisibilityStoreTests
{
    [Fact]
    public void IsVisible_ReturnsTrue_ForSourcesNotExplicitlyHidden()
    {
        var sut = new TelemetrySourceVisibilityStore();

        Assert.True(sut.IsVisible(SignalRowIds.Travel, TelemetrySourceKeys.Front));
    }

    [Fact]
    public void SetVisible_StoresHiddenState_PerRowAndSource()
    {
        var sut = new TelemetrySourceVisibilityStore();

        sut.SetVisible(SignalRowIds.Travel, TelemetrySourceKeys.Rear, visible: false);

        Assert.False(sut.IsVisible(SignalRowIds.Travel, TelemetrySourceKeys.Rear));
        Assert.True(sut.IsVisible(SignalRowIds.Travel, TelemetrySourceKeys.Front));
        Assert.True(sut.IsVisible(SignalRowIds.Velocity, TelemetrySourceKeys.Rear));
    }

    [Fact]
    public void SetVisible_RestoresHiddenSource()
    {
        var sut = new TelemetrySourceVisibilityStore();
        sut.SetVisible(SignalRowIds.Travel, TelemetrySourceKeys.Rear, visible: false);

        sut.SetVisible(SignalRowIds.Travel, TelemetrySourceKeys.Rear, visible: true);

        Assert.True(sut.IsVisible(SignalRowIds.Travel, TelemetrySourceKeys.Rear));
    }

    [Fact]
    public void Clear_RestoresAllHiddenSources()
    {
        var sut = new TelemetrySourceVisibilityStore();
        sut.SetVisible(SignalRowIds.Travel, TelemetrySourceKeys.Rear, visible: false);
        sut.SetVisible(SignalRowIds.Velocity, TelemetrySourceKeys.Front, visible: false);

        sut.Clear();

        Assert.True(sut.IsVisible(SignalRowIds.Travel, TelemetrySourceKeys.Rear));
        Assert.True(sut.IsVisible(SignalRowIds.Velocity, TelemetrySourceKeys.Front));
    }
}
