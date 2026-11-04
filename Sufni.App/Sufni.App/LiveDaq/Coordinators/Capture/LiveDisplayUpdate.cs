using System.Collections.Generic;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Sessions.Plots;

namespace Sufni.App.LiveDaq.Coordinators.Capture;

internal abstract record LiveDisplayUpdate(long Epoch)
{
    public sealed record Travel(
        long Epoch,
        double[] Times,
        double[] FrontTravel,
        double[] RearTravel) : LiveDisplayUpdate(Epoch);

    public sealed record Imu(
        long Epoch,
        IReadOnlyDictionary<LiveImuLocation, IReadOnlyList<double>> Times,
        IReadOnlyDictionary<LiveImuLocation, IReadOnlyList<double>> VibrationRms,
        FramePitchRollSeries? FramePitchRoll) : LiveDisplayUpdate(Epoch);
}
