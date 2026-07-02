using Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

using Sufni.App.Acquisition.Models;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.ViewModels.Editors;
namespace Sufni.App.Tests.LiveDaq.ViewModels.Editors;

public class LiveSessionSignalsWorkspaceViewModelTests
{
    [Fact]
    public void ApplySessionHeader_ClearsSourceVisibility_WhenSessionChanges()
    {
        var viewModel = new LiveSessionSignalsWorkspaceViewModel();
        viewModel.ApplySessionHeader(LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 1));
        viewModel.SourceVisibility.SetVisible(SignalRowIds.Travel, TelemetrySourceKeys.Rear, visible: false);

        viewModel.ApplySessionHeader(LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 2));

        Assert.True(viewModel.SourceVisibility.IsVisible(SignalRowIds.Travel, TelemetrySourceKeys.Rear));
    }

    [Fact]
    public void ApplySessionHeader_KeepsSourceVisibility_WhenSessionIsUnchanged()
    {
        var viewModel = new LiveSessionSignalsWorkspaceViewModel();
        viewModel.ApplySessionHeader(LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 1));
        viewModel.SourceVisibility.SetVisible(SignalRowIds.Travel, TelemetrySourceKeys.Rear, visible: false);

        viewModel.ApplySessionHeader(LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 1));

        Assert.False(viewModel.SourceVisibility.IsVisible(SignalRowIds.Travel, TelemetrySourceKeys.Rear));
    }

    [Fact]
    public void ApplySessionHeader_ClearsSourceVisibility_WhenSessionClears()
    {
        var viewModel = new LiveSessionSignalsWorkspaceViewModel();
        viewModel.SourceVisibility.SetVisible(SignalRowIds.Travel, TelemetrySourceKeys.Rear, visible: false);

        viewModel.ApplySessionHeader(null);

        Assert.True(viewModel.SourceVisibility.IsVisible(SignalRowIds.Travel, TelemetrySourceKeys.Rear));
    }
}
