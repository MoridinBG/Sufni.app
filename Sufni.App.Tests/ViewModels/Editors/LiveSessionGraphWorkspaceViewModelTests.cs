using Sufni.App.Models;
using Sufni.App.Tests.Services.LiveStreaming;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.ViewModels.Editors;

public class LiveSessionGraphWorkspaceViewModelTests
{
    [Fact]
    public void ApplySessionHeader_ClearsSourceVisibility_WhenSessionChanges()
    {
        var viewModel = new LiveSessionGraphWorkspaceViewModel();
        viewModel.ApplySessionHeader(LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 1));
        viewModel.SourceVisibility.SetVisible(TelemetryGraphRowIds.Travel, TelemetrySourceKeys.Rear, visible: false);

        viewModel.ApplySessionHeader(LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 2));

        Assert.True(viewModel.SourceVisibility.IsVisible(TelemetryGraphRowIds.Travel, TelemetrySourceKeys.Rear));
    }

    [Fact]
    public void ApplySessionHeader_KeepsSourceVisibility_WhenSessionIsUnchanged()
    {
        var viewModel = new LiveSessionGraphWorkspaceViewModel();
        viewModel.ApplySessionHeader(LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 1));
        viewModel.SourceVisibility.SetVisible(TelemetryGraphRowIds.Travel, TelemetrySourceKeys.Rear, visible: false);

        viewModel.ApplySessionHeader(LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 1));

        Assert.False(viewModel.SourceVisibility.IsVisible(TelemetryGraphRowIds.Travel, TelemetrySourceKeys.Rear));
    }

    [Fact]
    public void ApplySessionHeader_ClearsSourceVisibility_WhenSessionClears()
    {
        var viewModel = new LiveSessionGraphWorkspaceViewModel();
        viewModel.SourceVisibility.SetVisible(TelemetryGraphRowIds.Travel, TelemetrySourceKeys.Rear, visible: false);

        viewModel.ApplySessionHeader(null);

        Assert.True(viewModel.SourceVisibility.IsVisible(TelemetryGraphRowIds.Travel, TelemetrySourceKeys.Rear));
    }
}
