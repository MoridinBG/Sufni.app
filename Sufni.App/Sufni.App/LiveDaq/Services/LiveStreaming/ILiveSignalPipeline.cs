using System;
using System.Threading.Tasks;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

internal interface ILiveSignalPipeline : IAsyncDisposable
{
    IObservable<LiveSignalBatch> SignalBatches { get; }

    void Start();

    void AppendTravelSamples(long epoch, ReadOnlySpan<double> times, ReadOnlySpan<double> frontTravel, ReadOnlySpan<double> rearTravel);

    void AppendImuSamples(long epoch, LiveImuLocation location, ReadOnlySpan<double> times, ReadOnlySpan<double> vibrationRms);

    void AppendFramePitchRollSamples(long epoch, ReadOnlySpan<double> times, ReadOnlySpan<double> pitchDegrees, ReadOnlySpan<double> rollDegrees);

    void BeginEpoch(long epoch);
}
