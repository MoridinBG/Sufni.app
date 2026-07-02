using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.TestSupport.Async;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Models;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Analysis.Services;

public class RecordedSessionAnalysisResultStateTests
{
    [Fact]
    public async Task RequestAsync_PublishesAgain_WhenSynchronousRequestReusesPreviousKey()
    {
        var telemetry = new TelemetryData();
        var state = new RecordedSessionAnalysisResultState(
            new TestAnalysisComputer(),
            new InlineBackgroundTaskRunner(),
            new InlineUiThreadDispatcher(),
            () => telemetry);
        var changes = new List<RecordedSessionAnalysisResultChanged>();
        using var subscription = state.Connect().Subscribe(changes.Add);

        var fullInputs = CreateInputs(range: null);
        var rangedInputs = CreateInputs(new TelemetryTimeRange(0, 1));

        state.Invalidate(fullInputs);
        await state.RequestAsync(fullInputs.DampingPercentagesKey);
        state.Invalidate(rangedInputs);
        await state.RequestAsync(rangedInputs.DampingPercentagesKey);
        state.Invalidate(fullInputs);
        await state.RequestAsync(fullInputs.DampingPercentagesKey);

        Assert.Equal(3, changes.Count);
        var result = Assert.IsType<DampingPercentagesAnalysisResult>(changes[^1].Result);
        Assert.Equal(11, result.Percentages.FrontHscPercentage);
    }

    private static RecordedSessionAnalysisInputs CreateInputs(TelemetryTimeRange? range) =>
        new(
            TelemetryGeneration: 1,
            AnalysisRange: range,
            TravelDistributionMode: TravelDistributionMode.ActiveSuspension,
            VelocityAverageMode: VelocityAverageMode.SampleAveraged,
            BalanceDisplacementMode: BalanceDisplacementMode.Zenith,
            BalanceSpeedMode: BalanceSpeedMode.Both,
            DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
            DampingPercentages: SessionDampingPercentages.Empty,
            SessionInsightsTargetProfile: SessionInsightsTargetProfile.Trail);

    private sealed class TestAnalysisComputer : IRecordedSessionAnalysisComputer
    {
        public RecordedSessionAnalysisResult Compute(RecordedSessionAnalysisKey key, TelemetryData telemetry) =>
            new DampingPercentagesAnalysisResult(new SessionDampingPercentages(
                key.AnalysisRange.HasValue ? 10 : 11,
                null,
                null,
                null,
                null,
                null,
                null,
                null));
    }
}
