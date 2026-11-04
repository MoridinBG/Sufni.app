using Sufni.App.Sessions.Analysis.ViewModels.Editors;
using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Shared.Models;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal sealed class RecordedSessionAnalysisDemand : IAsyncDisposable
{
    private readonly IRecordedSessionAnalysisResultState analysisResultState;
    private readonly IDisposable analysisResultSubscription;
    private readonly IDisposable intentSubscription;
    private readonly Func<int, bool> isInsightsPage;
    private readonly Action<AnalysisAvailabilityResult> publishAvailability;
    private readonly Action<SelectionHighlightsAnalysisResult> publishHighlights;
    private readonly Action<SessionDampingPercentages> publishPercentages;
    private readonly Action<SessionInsightsResult> publishInsights;
    private readonly Action<FailurePresentation> reportError;
    private readonly CancellationToken lifetime;
    private RecordedSessionEditorState currentState = RecordedSessionEditorState.CreateInitial();
    private int telemetryGeneration;
    private bool insightsRequested;

    public RecordedSessionAnalysisDemand(IObservable<RecordedSessionEditorIntent> intents, IRecordedSessionAnalysisResultStateFactory factory,
        Func<int, bool> isInsightsPage, Action<AnalysisAvailabilityResult> publishAvailability,
        Action<SelectionHighlightsAnalysisResult> publishHighlights, Action<SessionDampingPercentages> publishPercentages,
        Action<SessionInsightsResult> publishInsights, Action<FailurePresentation> reportError, CancellationToken lifetime)
    {
        this.isInsightsPage = isInsightsPage;
        this.publishAvailability = publishAvailability;
        this.publishHighlights = publishHighlights;
        this.publishPercentages = publishPercentages;
        this.publishInsights = publishInsights;
        this.reportError = reportError;
        this.lifetime = lifetime;
        analysisResultState = factory.Create(() => currentState.TelemetryData);
        analysisResultState.Invalidate(CreateCurrentAnalysisInputs());
        analysisResultSubscription = analysisResultState.Connect().Subscribe(OnAnalysisResultChanged);
        intentSubscription = intents.OfType<RecordedSessionEditorIntent.RequestSessionInsights>().Subscribe(_ => RequestInsights());
    }

    public IRecordedSessionAnalysisResultState ResultState => analysisResultState;
    public void OnPagesChanged() => RequestVisibleInsights();
    public void Stop() => analysisResultState.Dispose();

    public async ValueTask DisposeAsync()
    {
        intentSubscription.Dispose();
        analysisResultSubscription.Dispose();
        await analysisResultState.DisposeAsync();
    }

    public void Prepare(RecordedSessionEditorState state)
    {
        var previous = currentState;
        currentState = state;
        if (previous.TelemetryData != state.TelemetryData) telemetryGeneration++;
        UpdateAnalysisRequests(previous);
    }

    private RecordedSessionAnalysisInputs CreateCurrentAnalysisInputs() =>
        new(
            telemetryGeneration,
            currentState.Intent.AnalysisRange,
            currentState.Preferences.Analysis.TravelDistributionMode,
            currentState.Preferences.Analysis.VelocityAverageMode,
            currentState.Preferences.Analysis.BalanceDisplacementMode,
            currentState.Preferences.Analysis.BalanceSpeedMode,
            currentState.Intent.DampingSpeedCutoffs,
            currentState.Presentation.PlotDampingSpeedCutoffs,
            currentState.Preferences.Analysis.SessionInsightsTargetProfile)
        {
            FrontSelection = currentState.AnalysisSelection.ActiveFront,
            RearSelection = currentState.AnalysisSelection.ActiveRear,
        };

    private void UpdateAnalysisRequests(RecordedSessionEditorState previous)
    {
        var inputs = CreateCurrentAnalysisInputs();
        var held = analysisResultState.CurrentInputs;
        if (held != inputs)
        {
            analysisResultState.Invalidate(inputs);
            if (currentState.TelemetryData is not null)
            {
                if (held?.AvailabilityKey != inputs.AvailabilityKey)
                    RequestAnalysisResult(inputs.AvailabilityKey);
                if (held?.SelectionHighlightsKey != inputs.SelectionHighlightsKey && (inputs.FrontSelection is not null || inputs.RearSelection is not null))
                    RequestAnalysisResult(inputs.SelectionHighlightsKey);
                if (held?.DampingPercentagesKey != inputs.DampingPercentagesKey)
                    RequestAnalysisResult(inputs.DampingPercentagesKey);
                if ((insightsRequested || isInsightsPage(currentState.Intent.SelectedPageIndex)) && held?.SessionInsightsKey != inputs.SessionInsightsKey)
                    RequestInsights();
            }
        }
        if (previous.Intent.SelectedPageIndex != currentState.Intent.SelectedPageIndex)
            RequestVisibleInsights();
    }

    private void RequestAnalysisResult(RecordedSessionAnalysisKey key)
    {
        if (analysisResultState.Get(key) is { } cached)
        {
            ApplyAnalysisResult(key, cached);
            return;
        }
        _ = analysisResultState.RequestAsync(key);
    }

    public void RequestInsights()
    {
        if (lifetime.IsCancellationRequested) return;
        insightsRequested = true;
        if (currentState.TelemetryData is not null && analysisResultState.CurrentInputs is { } inputs)
            RequestAnalysisResult(inputs.SessionInsightsKey);
    }

    private void RequestVisibleInsights()
    {
        if (isInsightsPage(currentState.Intent.SelectedPageIndex)) RequestInsights();
    }

    private void OnAnalysisResultChanged(RecordedSessionAnalysisResultChanged change)
    {
        if (analysisResultState.CurrentInputs is not { } inputs || !change.Key.Matches(inputs))
        {
            return;
        }

        if (change.Failure is { } failure)
        {
            reportError(FailurePresentation.From(failure.Reason, failure.Details));
            return;
        }

        if (change.Result is not null)
        {
            ApplyAnalysisResult(change.Key, change.Result);
        }
    }

    private void ApplyAnalysisResult(
        RecordedSessionAnalysisKey key,
        RecordedSessionAnalysisResult result)
    {
        if (analysisResultState.CurrentInputs is not { } inputs || !key.Matches(inputs))
        {
            return;
        }

        switch (result)
        {
            case AnalysisAvailabilityResult availability:
                publishAvailability(availability);
                break;
            case SelectionHighlightsAnalysisResult highlights:
                publishHighlights(highlights);
                break;
            case DampingPercentagesAnalysisResult damping:
                publishPercentages(damping.Percentages);

                break;
            case SessionInsightsAnalysisResult insights:
                publishInsights(insights.Insights);
                break;
        }
    }

}
