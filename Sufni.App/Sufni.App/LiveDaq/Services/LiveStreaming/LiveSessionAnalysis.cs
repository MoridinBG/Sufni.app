using System.Collections.Frozen;
using System.Collections.Generic;
using Sufni.App.Bikes.Models;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.LiveDaq.Coordinators.Capture;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public sealed record LiveAnalysisOptions(
    TravelDistributionMode TravelMode,
    VelocityAverageMode VelocityMode,
    BalanceDisplacementMode BalanceDisplacementMode,
    BalanceSpeedMode BalanceSpeedMode,
    DampingSpeedCutoffs DampingCutoffs,
    DampingSpeedCutoffs PlotDampingCutoffs)
{
    public static LiveAnalysisOptions Default(DampingSpeedCutoffs cutoffs) => new(
        TravelDistributionMode.ActiveSuspension, VelocityAverageMode.SampleAveraged,
        BalanceDisplacementMode.Zenith, BalanceSpeedMode.Both, cutoffs, cutoffs);
}

public sealed record LiveSessionAnalysis(
    LiveAnalysisKey Key,
    TelemetryData Telemetry,
    RecordedSessionAnalysisInputs Inputs,
    SessionDampingPercentages DampingPercentages,
    FrozenDictionary<RecordedSessionAnalysisKey, RecordedSessionAnalysisResult> Results)
{
    public LiveAnalysisOptions Options => Key.Options;

    internal static LiveSessionAnalysis Compute(LiveAnalysisKey key,
        TelemetryData telemetry, SessionDampingPercentages percentages)
    {
        var options = key.Options;
        var inputs = new RecordedSessionAnalysisInputs(key.CaptureRevision, null, options.TravelMode, options.VelocityMode,
            options.BalanceDisplacementMode, options.BalanceSpeedMode, options.DampingCutoffs, options.PlotDampingCutoffs,
            SessionInsightsTargetProfile.Trail);
        var results = new Dictionary<RecordedSessionAnalysisKey, RecordedSessionAnalysisResult>();
        foreach (var side in new[] { SuspensionType.Front, SuspensionType.Rear })
        {
            foreach (var family in new[] { RecordedSessionAnalysisFamily.TravelDistribution,
                RecordedSessionAnalysisFamily.TravelFrequencyDistribution, RecordedSessionAnalysisFamily.VelocityDistribution,
                RecordedSessionAnalysisFamily.DeepTravelDistribution })
                Add(inputs.CreateKey(family, suspensionType: side));
            foreach (var direction in new[] { BalanceType.Compression, BalanceType.Rebound })
            {
                Add(inputs.CreateKey(RecordedSessionAnalysisFamily.StrokeLengthDistribution, side, direction));
                Add(inputs.CreateKey(RecordedSessionAnalysisFamily.StrokeSpeedDistribution, side, direction));
            }
        }
        Add(inputs.CreateKey(RecordedSessionAnalysisFamily.Balance, balanceType: BalanceType.Compression));
        Add(inputs.CreateKey(RecordedSessionAnalysisFamily.Balance, balanceType: BalanceType.Rebound));
        return new LiveSessionAnalysis(key, telemetry, inputs, percentages, results.ToFrozenDictionary());

        void Add(RecordedSessionAnalysisKey key) => results.Add(key, RecordedSessionAnalysisComputer.ComputePlot(key, telemetry));
    }
}

public enum LiveAnalysisFailureReason { Calculation }

public sealed record LiveAnalysisFailure(LiveAnalysisFailureReason Reason, string Details);
