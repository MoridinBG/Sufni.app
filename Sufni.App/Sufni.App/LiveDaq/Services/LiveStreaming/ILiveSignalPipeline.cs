using System;
using System.Threading.Tasks;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

internal interface ILiveSignalPipeline : IAsyncDisposable
{
    IObservable<LiveSignalBatch> SignalBatches { get; }

    void Start();

    void AppendTravelSamples(ReadOnlySpan<double> times, ReadOnlySpan<double> frontTravel, ReadOnlySpan<double> rearTravel);

    void AppendImuSamples(LiveImuLocation location, ReadOnlySpan<double> times, ReadOnlySpan<double> vibrationRms);

    void AppendFramePitchRollSamples(ReadOnlySpan<double> times, ReadOnlySpan<double> pitchDegrees, ReadOnlySpan<double> rollDegrees);

    void Reset();
}
