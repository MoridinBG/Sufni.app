using System;
using Sufni.App.SessionGraphs;
using Serilog;

namespace Sufni.App.Services.LiveStreaming;

internal sealed class LiveGraphPipelineFactory
{
    public ILiveGraphPipeline Create()
    {
        return new LiveGraphPipeline(
            TimeSpan.FromMilliseconds(SessionGraphSettings.LiveGraphRefreshIntervalMs),
            Log.ForContext<LiveGraphPipeline>());
    }
}
