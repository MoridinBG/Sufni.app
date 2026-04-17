using System;
using System.Threading.Tasks;

namespace Sufni.App.Services.LiveStreaming;

internal interface ILiveGraphPipeline : IAsyncDisposable
{
    IObservable<LiveGraphBatch> GraphBatches { get; }

    void Start();

    void AppendTravelSamples(ReadOnlySpan<double> times, ReadOnlySpan<double> frontTravel, ReadOnlySpan<double> rearTravel);

    void AppendImuSamples(LiveImuLocation location, ReadOnlySpan<double> times, ReadOnlySpan<double> magnitudes);

    void Reset();
}
