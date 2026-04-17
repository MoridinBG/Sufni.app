using System;
using Serilog;

namespace Sufni.App.Services.LiveStreaming;

internal sealed class LiveGraphPipelineFactory : ILiveGraphPipelineFactory
{
    public ILiveGraphPipeline Create()
    {
        return new LiveGraphPipeline(
            TimeSpan.FromMilliseconds(LiveSessionRefreshCadence.GraphRefreshIntervalMs),
            Log.ForContext<LiveGraphPipeline>());
    }
}
