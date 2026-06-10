using System;
using Sufni.App.Plots;
using Serilog;

namespace Sufni.App.Services.LiveStreaming;

internal sealed class LiveGraphPipelineFactory
{
    public ILiveGraphPipeline Create()
    {
        return new LiveGraphPipeline(
            TimeSpan.FromMilliseconds(PlotSettings.LiveGraphRefreshIntervalMs),
            Log.ForContext<LiveGraphPipeline>());
    }
}
