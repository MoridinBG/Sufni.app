using System;
using Serilog;

using Sufni.App.Shared.Plots;
namespace Sufni.App.LiveDaq.Services.LiveStreaming;

internal sealed class LiveGraphPipelineFactory
{
    public ILiveGraphPipeline Create()
    {
        return new LiveGraphPipeline(
            TimeSpan.FromMilliseconds(PlotSettings.LiveGraphRefreshIntervalMs),
            Log.ForContext<LiveGraphPipeline>());
    }
}
