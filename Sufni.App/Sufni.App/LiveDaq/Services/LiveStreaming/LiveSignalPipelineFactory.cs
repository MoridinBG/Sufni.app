using System;
using Serilog;

using Sufni.App.Shared.Plots;
namespace Sufni.App.LiveDaq.Services.LiveStreaming;

internal sealed class LiveSignalPipelineFactory
{
    public ILiveSignalPipeline Create()
    {
        return new LiveSignalPipeline(
            TimeSpan.FromMilliseconds(PlotSettings.LiveSignalRefreshIntervalMs),
            Log.ForContext<LiveSignalPipeline>());
    }
}
