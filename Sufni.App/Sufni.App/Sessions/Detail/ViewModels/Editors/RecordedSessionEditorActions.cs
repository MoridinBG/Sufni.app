using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal abstract record RecordedSessionEditorIntent
{
    public sealed record SelectPageIndex(int PageIndex) : RecordedSessionEditorIntent;

    public sealed record SetAnalysisRange(TelemetryTimeRange? Range) : RecordedSessionEditorIntent;

    public sealed record SetAnalysisRangeBoundary(double Seconds) : RecordedSessionEditorIntent;

    public sealed record ClearAnalysisRange : RecordedSessionEditorIntent;

    public sealed record SelectAnalysisRange(TelemetryRangeSelection Selection) : RecordedSessionEditorIntent;

    public sealed record ClearAnalysisSelection : RecordedSessionEditorIntent;

    public sealed record SetTravelDistributionMode(TravelDistributionMode Mode) : RecordedSessionEditorIntent;

    public sealed record SetBalanceDisplacementMode(BalanceDisplacementMode Mode) : RecordedSessionEditorIntent;

    public sealed record SetBalanceSpeedMode(BalanceSpeedMode Mode) : RecordedSessionEditorIntent;

    public sealed record SetVelocityAverageMode(VelocityAverageMode Mode) : RecordedSessionEditorIntent;

    public sealed record SetSessionInsightsTargetProfile(SessionInsightsTargetProfile Profile) : RecordedSessionEditorIntent;

    public sealed record SetDampingSpeedCutoffs(DampingSpeedCutoffs Cutoffs) : RecordedSessionEditorIntent;

    public sealed record SetSignalDisplayPreferences(SignalDisplayPreferences Preferences) : RecordedSessionEditorIntent;

    public sealed record SetSignalLayoutPreferences(SignalLayoutPreferences Preferences) : RecordedSessionEditorIntent;

    public sealed record SetLayoutPreferences(SessionLayoutPreferences Preferences) : RecordedSessionEditorIntent;
}

internal sealed class RecordedSessionEditorActions : IDisposable
{
    private readonly ISubject<RecordedSessionEditorIntent> intents =
        Subject.Synchronize(new Subject<RecordedSessionEditorIntent>());
    private bool disposed;

    public IObservable<RecordedSessionEditorIntent> Intents => intents.AsObservable();

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

    public void ClearAnalysisRange() =>
        Emit(new RecordedSessionEditorIntent.ClearAnalysisRange());

    public void SelectAnalysisRange(TelemetryRangeSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        Emit(new RecordedSessionEditorIntent.SelectAnalysisRange(selection));
    }

    public void ClearAnalysisSelection() =>
        Emit(new RecordedSessionEditorIntent.ClearAnalysisSelection());

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

    public void SetDampingSpeedCutoffs(DampingSpeedCutoffs cutoffs)
    {
        ArgumentNullException.ThrowIfNull(cutoffs);
        Emit(new RecordedSessionEditorIntent.SetDampingSpeedCutoffs(cutoffs.ClampValues()));
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
