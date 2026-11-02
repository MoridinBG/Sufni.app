using System.Collections.Frozen;
using System.Collections.Generic;
using Sufni.App.Bikes.Models;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Shared.Common;
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
    long Revision,
    LiveAnalysisOptions Options,
    RecordedSessionAnalysisInputs Inputs,
    SessionDampingPercentages DampingPercentages,
    FrozenDictionary<RecordedSessionAnalysisKey, RecordedSessionAnalysisResult> Results,
    SurfacePresentationState FrontState,
    SurfacePresentationState RearState,
    SurfacePresentationState CompressionState,
    SurfacePresentationState ReboundState)
{
    internal static LiveSessionAnalysis Compute(long revision, LiveAnalysisOptions options,
        TelemetryData telemetry, SessionDampingPercentages percentages, bool frontExpected, bool rearExpected)
    {
        var inputs = new RecordedSessionAnalysisInputs(revision, null, options.TravelMode, options.VelocityMode,
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
        return new LiveSessionAnalysis(revision, options, inputs, percentages, results.ToFrozenDictionary(),
            AnalysisSurfaceState.ForSuspension(frontExpected, telemetry, SuspensionType.Front),
            AnalysisSurfaceState.ForSuspension(rearExpected, telemetry, SuspensionType.Rear),
            AnalysisSurfaceState.ForBalance(frontExpected && rearExpected, telemetry, BalanceType.Compression),
            AnalysisSurfaceState.ForBalance(frontExpected && rearExpected, telemetry, BalanceType.Rebound));

        void Add(RecordedSessionAnalysisKey key) => results.Add(key, RecordedSessionAnalysisComputer.ComputePlot(key, telemetry));
    }
}

public enum LiveAnalysisFailureReason { Calculation, Rendering }

public sealed record LiveAnalysisFailure(LiveAnalysisFailureReason Reason, string Details)
{
    public string Message => Reason switch
    {
        LiveAnalysisFailureReason.Calculation => "Live analysis could not be calculated. Capture continues; new samples will retry analysis.",
        LiveAnalysisFailureReason.Rendering => "Live analysis could not be rendered. Capture continues.",
        _ => throw new System.ArgumentOutOfRangeException(nameof(Reason))
    };
}
