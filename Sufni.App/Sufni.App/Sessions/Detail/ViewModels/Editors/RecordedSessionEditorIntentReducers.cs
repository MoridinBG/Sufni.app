using System;
using Sufni.App.Infrastructure;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal static class RecordedSessionEditorIntentReducers
{
    public static Func<SessionPreferences, SessionPreferences>? CreatePreferenceUpdate(
        RecordedSessionEditorIntent intent)
    {
        return intent switch
        {
            RecordedSessionEditorIntent.SetTravelDistributionMode set =>
                current => current with
                {
                    Analysis = current.Analysis with { TravelDistributionMode = set.Mode },
                },
            RecordedSessionEditorIntent.SetBalanceDisplacementMode set =>
                current => current with
                {
                    Analysis = current.Analysis with { BalanceDisplacementMode = set.Mode },
                },
            RecordedSessionEditorIntent.SetBalanceSpeedMode set =>
                current => current with
                {
                    Analysis = current.Analysis with { BalanceSpeedMode = set.Mode },
                },
            RecordedSessionEditorIntent.SetVelocityAverageMode set =>
                current => current with
                {
                    Analysis = current.Analysis with { VelocityAverageMode = set.Mode },
                },
            RecordedSessionEditorIntent.SetSessionInsightsTargetProfile set =>
                current => current with
                {
                    Analysis = current.Analysis with { SessionInsightsTargetProfile = set.Profile },
                },
            RecordedSessionEditorIntent.SetSignalDisplayPreferences set =>
                current => current with { SignalDisplay = set.Preferences },
            RecordedSessionEditorIntent.SetSignalLayoutPreferences set =>
                current => current with { SignalLayout = set.Preferences },
            RecordedSessionEditorIntent.SetLayoutPreferences set =>
                current => current with { Layout = set.Preferences },
            _ => null,
        };
    }

    public static bool IsPreferencePersistenceIntent(RecordedSessionEditorIntent intent)
    {
        return CreatePreferenceUpdate(intent) is not null;
    }
}
