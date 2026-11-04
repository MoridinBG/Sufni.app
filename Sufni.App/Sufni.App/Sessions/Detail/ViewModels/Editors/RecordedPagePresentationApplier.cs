using System;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Sufni.App.Sessions.Insights.ViewModels.SessionPages;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal sealed class RecordedPagePresentationApplier : IDisposable
{
    private readonly ObservableCollection<PageViewModelBase> pages;
    private readonly SpringPageViewModel springPage;
    private readonly DampingPageViewModel dampingPage;
    private readonly BalancePageViewModel balancePage;
    private readonly VibrationPageViewModel vibrationPage;
    private readonly SessionInsightsPageViewModel analysisPage;
    private readonly NotesPageViewModel notesPage;
    private readonly PreferencesPageViewModel preferencesPage;
    private readonly IDisposable subscription;
    private RecordedSessionEditorState currentState = RecordedSessionEditorState.CreateInitial();

    public RecordedPagePresentationApplier(
        ObservableCollection<PageViewModelBase> pages,
        SpringPageViewModel springPage,
        DampingPageViewModel dampingPage,
        BalancePageViewModel balancePage,
        VibrationPageViewModel vibrationPage,
        SessionInsightsPageViewModel analysisPage,
        NotesPageViewModel notesPage,
        PreferencesPageViewModel preferencesPage,
        IObservable<RecordedSessionEditorState> state)
    {
        this.pages = pages;
        this.springPage = springPage;
        this.dampingPage = dampingPage;
        this.balancePage = balancePage;
        this.vibrationPage = vibrationPage;
        this.analysisPage = analysisPage;
        this.notesPage = notesPage;
        this.preferencesPage = preferencesPage;
        subscription = state.Subscribe(ApplyState);
    }

    public void Dispose() => subscription.Dispose();

    private void ApplyState(RecordedSessionEditorState state)
    {
        var previous = currentState;
        currentState = state;
        var analysis = state.Presentation.Analysis;
        springPage.FrontDistributionState = analysis.FrontAnalysis;
        springPage.RearDistributionState = analysis.RearAnalysis;
        dampingPage.FrontDistributionState = analysis.FrontAnalysis;
        dampingPage.RearDistributionState = analysis.RearAnalysis;
        balancePage.CompressionBalanceState = analysis.CompressionBalance;
        balancePage.ReboundBalanceState = analysis.ReboundBalance;
        EnsureBalancePage(analysis.FullSessionBalanceAvailable);
        preferencesPage.RefreshSignalDisplayPreferences(previous.Preferences.SignalDisplay, state.Preferences.SignalDisplay);
        if (previous.ProcessingOptions != state.ProcessingOptions)
            preferencesPage.RefreshProcessingOptions();

        if (previous.TelemetryData != state.TelemetryData)
        {
            preferencesPage.SampleRate = state.TelemetryData?.Metadata.SampleRate ?? 0;
            notesPage.SetTemperatureAverages(state.TelemetryData?.TemperatureAverages ?? []);
        }

        if (previous.Presentation.SignalAvailability != state.Presentation.SignalAvailability)
        {
            var availability = state.Presentation.SignalAvailability;
            preferencesPage.ApplySignalAvailability(
                availability.Travel,
                availability.Velocity,
                availability.Imu,
                availability.PitchRoll,
                availability.Speed,
                availability.Elevation);
        }

        if (previous.Presentation.DampingPercentages != state.Presentation.DampingPercentages)
            dampingPage.ApplyDampingPercentages(state.Presentation.DampingPercentages);
    }

    private void EnsureBalancePage(bool balanceAvailable)
    {
        var containsBalancePage = pages.Contains(balancePage);
        if (balanceAvailable)
        {
            if (containsBalancePage)
            {
                return;
            }

            var insertIndex = pages.IndexOf(vibrationPage);
            if (insertIndex < 0)
            {
                insertIndex = pages.IndexOf(analysisPage);
            }

            if (insertIndex < 0)
            {
                insertIndex = pages.IndexOf(notesPage);
            }

            if (insertIndex < 0)
            {
                pages.Add(balancePage);
            }
            else
            {
                pages.Insert(insertIndex, balancePage);
            }

            return;
        }

        if (containsBalancePage)
        {
            pages.Remove(balancePage);
        }
    }
}
