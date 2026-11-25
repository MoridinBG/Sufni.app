using System;
using Sufni.App.Terrain.Models;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Sufni.App.Bikes.Models;
using Sufni.Telemetry;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal closed record RecordedSessionEditorIntent
{
    public sealed record ToggleRoughness(RoughnessClass Class) : RecordedSessionEditorIntent;
    public sealed record ToggleGrade(GradeBand Band) : RecordedSessionEditorIntent;
    public sealed record SelectTerrainPreset(TerrainPreset Preset) : RecordedSessionEditorIntent;
    public sealed record ResetTerrainScope : RecordedSessionEditorIntent;
    public sealed record BeginSegmentComparison(Guid MatchedSessionId) : RecordedSessionEditorIntent;
    public sealed record EndSegmentComparison : RecordedSessionEditorIntent;
    public sealed record SetMapTrackColoring(MapTrackColoring Value) : RecordedSessionEditorIntent;
    public sealed record SetBaseline(TerrainProfileKey ProfileKey, TerrainBaseline Baseline) : RecordedSessionEditorIntent;
    public sealed record SetTerrainCatalog(TerrainScaleCatalog Catalog) : RecordedSessionEditorIntent;
    public sealed record SetTerrain(TerrainStatus.Current Expected, TerrainScaleKey Scale, RecordedTerrainState Value) : RecordedSessionEditorIntent;

    public sealed record PreviewProcessingOption(int WindowMilliseconds) : RecordedSessionEditorIntent;
    public sealed record CommitProcessingOption : RecordedSessionEditorIntent;
    public sealed record CompleteProcessingOption(RecordedProcessingDraft Draft, SessionSnapshot Session) : RecordedSessionEditorIntent;

    public sealed record SelectPageIndex(int PageIndex) : RecordedSessionEditorIntent;

    public sealed record SetAnalysisRange(TelemetryTimeRange? Range) : RecordedSessionEditorIntent;

    public sealed record SetAnalysisRangeBoundary(double Seconds) : RecordedSessionEditorIntent;

    public sealed record SetAnalysisRangeStartBoundary(double Seconds) : RecordedSessionEditorIntent;

    public sealed record SetAnalysisRangeEndBoundary(double Seconds) : RecordedSessionEditorIntent;

    public sealed record ClearAnalysisRange : RecordedSessionEditorIntent;

    public sealed record SelectAnalysisRange(TelemetryRangeSelection Selection) : RecordedSessionEditorIntent;

    public sealed record ClearAnalysisSelection : RecordedSessionEditorIntent;

    public sealed record RequestSessionInsights : RecordedSessionEditorIntent;

    public sealed record SetTravelDistributionMode(TravelDistributionMode Mode) : RecordedSessionEditorIntent;

    public sealed record SetBalanceDisplacementMode(BalanceDisplacementMode Mode) : RecordedSessionEditorIntent;

    public sealed record SetBalanceSpeedMode(BalanceSpeedMode Mode) : RecordedSessionEditorIntent;

    public sealed record SetVelocityAverageMode(VelocityAverageMode Mode) : RecordedSessionEditorIntent;

    public sealed record SetSessionInsightsTargetProfile(SessionInsightsTargetProfile Profile) : RecordedSessionEditorIntent;

    public sealed record SetDampingSpeedCutoffs(DampingSpeedCutoffs Cutoffs, bool UpdatePlots) : RecordedSessionEditorIntent;

    public sealed record SetSignalDisplayPreferences(SignalDisplayPreferences Preferences) : RecordedSessionEditorIntent;

    public sealed record SetSignalLayoutPreferences(SignalLayoutPreferences Preferences) : RecordedSessionEditorIntent;

    public sealed record SetLayoutPreferences(SessionLayoutPreferences Preferences) : RecordedSessionEditorIntent;

    public sealed record SetSignalPresentation(RecordedSignalPresentationState State) : RecordedSessionEditorIntent;
}

internal sealed class RecordedSessionEditorActions : IDisposable
{
    private readonly ISubject<RecordedSessionEditorIntent> intents =
        Subject.Synchronize(new Subject<RecordedSessionEditorIntent>());
    private bool disposed;

    public IObservable<RecordedSessionEditorIntent> Intents => intents.AsObservable();

    public void PreviewProcessingOption(int windowMilliseconds) =>
        Emit(new RecordedSessionEditorIntent.PreviewProcessingOption(windowMilliseconds));

    public void CommitProcessingOption() => Emit(new RecordedSessionEditorIntent.CommitProcessingOption());

    public void CompleteProcessingOption(RecordedProcessingDraft draft, SessionSnapshot session) =>
        Emit(new RecordedSessionEditorIntent.CompleteProcessingOption(draft, session));

    public void SelectPageIndex(int pageIndex) =>
        Emit(new RecordedSessionEditorIntent.SelectPageIndex(Math.Max(0, pageIndex)));

    public void SetAnalysisRange(TelemetryTimeRange? range) =>
        Emit(new RecordedSessionEditorIntent.SetAnalysisRange(range));

    public void SetAnalysisRangeBoundary(double seconds)
    {
        if (double.IsFinite(seconds))
        {
            Emit(new RecordedSessionEditorIntent.SetAnalysisRangeBoundary(seconds));
        }
    }

    public void SetAnalysisRangeStartBoundary(double seconds)
    {
        if (double.IsFinite(seconds))
        {
            Emit(new RecordedSessionEditorIntent.SetAnalysisRangeStartBoundary(seconds));
        }
    }

    public void SetAnalysisRangeEndBoundary(double seconds)
    {
        if (double.IsFinite(seconds))
        {
            Emit(new RecordedSessionEditorIntent.SetAnalysisRangeEndBoundary(seconds));
        }
    }

    public void ClearAnalysisRange() =>
        Emit(new RecordedSessionEditorIntent.ClearAnalysisRange());

    public void SelectAnalysisRange(TelemetryRangeSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        Emit(new RecordedSessionEditorIntent.SelectAnalysisRange(selection));
    }

    public void ClearAnalysisSelection() =>
        Emit(new RecordedSessionEditorIntent.ClearAnalysisSelection());

    public void RequestSessionInsights() =>
        Emit(new RecordedSessionEditorIntent.RequestSessionInsights());

    public void SetTravelDistributionMode(TravelDistributionMode mode) =>
        Emit(new RecordedSessionEditorIntent.SetTravelDistributionMode(mode));

    public void SetBalanceDisplacementMode(BalanceDisplacementMode mode) =>
        Emit(new RecordedSessionEditorIntent.SetBalanceDisplacementMode(mode));

    public void SetBalanceSpeedMode(BalanceSpeedMode mode) =>
        Emit(new RecordedSessionEditorIntent.SetBalanceSpeedMode(mode));

    public void SetVelocityAverageMode(VelocityAverageMode mode) =>
        Emit(new RecordedSessionEditorIntent.SetVelocityAverageMode(mode));

    public void SetSessionInsightsTargetProfile(SessionInsightsTargetProfile profile) =>
        Emit(new RecordedSessionEditorIntent.SetSessionInsightsTargetProfile(profile));

    public void SetDampingSpeedCutoffs(DampingSpeedCutoffs cutoffs, bool updatePlots = false)
    {
        ArgumentNullException.ThrowIfNull(cutoffs);
        Emit(new RecordedSessionEditorIntent.SetDampingSpeedCutoffs(cutoffs.ClampValues(), updatePlots));
    }

    public void SetSignalDisplayPreferences(SignalDisplayPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Emit(new RecordedSessionEditorIntent.SetSignalDisplayPreferences(preferences));
    }

    public void SetSignalLayoutPreferences(SignalLayoutPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Emit(new RecordedSessionEditorIntent.SetSignalLayoutPreferences(preferences));
    }

    public void SetLayoutPreferences(SessionLayoutPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Emit(new RecordedSessionEditorIntent.SetLayoutPreferences(preferences));
    }

    public void ToggleRoughness(RoughnessClass value) => Emit(new RecordedSessionEditorIntent.ToggleRoughness(value));
    public void ToggleGrade(GradeBand value) => Emit(new RecordedSessionEditorIntent.ToggleGrade(value));
    public void SelectTerrainPreset(TerrainPreset value) => Emit(new RecordedSessionEditorIntent.SelectTerrainPreset(value));
    public void ResetTerrainScope() => Emit(new RecordedSessionEditorIntent.ResetTerrainScope());
    public void BeginSegmentComparison(Guid id) => Emit(new RecordedSessionEditorIntent.BeginSegmentComparison(id));
    public void EndSegmentComparison() => Emit(new RecordedSessionEditorIntent.EndSegmentComparison());
    public void SetMapTrackColoring(MapTrackColoring value) => Emit(new RecordedSessionEditorIntent.SetMapTrackColoring(value));

    public void SetBaseline(TerrainProfileKey key, TerrainBaseline baseline) => Emit(new RecordedSessionEditorIntent.SetBaseline(key, baseline));

    public void SetTerrainCatalog(TerrainScaleCatalog catalog) => Emit(new RecordedSessionEditorIntent.SetTerrainCatalog(catalog));

    public void SetTerrain(TerrainStatus.Current expected, TerrainScaleKey scale, RecordedTerrainState value) =>
        Emit(new RecordedSessionEditorIntent.SetTerrain(expected, scale, value));

    public void SetSignalPresentation(RecordedSignalPresentationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Emit(new RecordedSessionEditorIntent.SetSignalPresentation(state));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        intents.OnCompleted();
    }

    private void Emit(RecordedSessionEditorIntent intent)
    {
        if (!disposed)
        {
            intents.OnNext(intent);
        }
    }
}
