using CommunityToolkit.Mvvm.Input;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using System.Collections.Generic;
using System;

namespace Sufni.App.Sessions.Signals.ViewModels.Editors;

/// <summary>Projects the current signal state into the twelve plot-header toggles.</summary>
internal sealed class SignalRowActionsController
{
    private readonly List<(SignalRowAction Action, Func<RecordedSignalPresentationState, bool> IsChecked, bool Airtime)> bindings = [];

    public IReadOnlyList<SignalRowAction> TravelHeaderActions { get; }
    public IReadOnlyList<SignalRowAction> VelocityHeaderActions { get; }
    public IReadOnlyList<SignalRowAction> ImuHeaderActions { get; }
    public IReadOnlyList<SignalRowAction> PitchRollHeaderActions { get; }
    public IReadOnlyList<SignalRowAction> SpeedHeaderActions { get; }
    public IReadOnlyList<SignalRowAction> ElevationHeaderActions { get; }

    public SignalRowActionsController(Func<RecordedSignalPresentationState> getSignals, RecordedSessionEditorActions actions)
    {
        TravelHeaderActions =
        [
            Create("travel_airtime", true, state => state.ShowAirtime, (state, value) => state with { ShowAirtime = value }),
            Create("travel_analysis_selection", false, state => state.ShowAnalysisSelection, (state, value) => state with { ShowAnalysisSelection = value }),
        ];
        VelocityHeaderActions =
        [
            Create("velocity_airtime", true, state => state.ShowVelocityAirtime, (state, value) => state with { ShowVelocityAirtime = value }),
            Create("velocity_analysis_selection", false, state => state.ShowVelocityAnalysisSelection, (state, value) => state with { ShowVelocityAnalysisSelection = value }),
        ];
        ImuHeaderActions =
        [
            Create("imu_airtime", true, state => state.ShowImuAirtime, (state, value) => state with { ShowImuAirtime = value }),
            Create("imu_analysis_selection", false, state => state.ShowImuAnalysisSelection, (state, value) => state with { ShowImuAnalysisSelection = value }),
        ];
        PitchRollHeaderActions =
        [
            Create("pitch_roll_airtime", true, state => state.ShowPitchRollAirtime, (state, value) => state with { ShowPitchRollAirtime = value }),
            Create("pitch_roll_analysis_selection", false, state => state.ShowPitchRollAnalysisSelection, (state, value) => state with { ShowPitchRollAnalysisSelection = value }),
        ];
        SpeedHeaderActions =
        [
            Create("speed_airtime", true, state => state.ShowSpeedAirtime, (state, value) => state with { ShowSpeedAirtime = value }),
            Create("speed_analysis_selection", false, state => state.ShowSpeedAnalysisSelection, (state, value) => state with { ShowSpeedAnalysisSelection = value }),
        ];
        ElevationHeaderActions =
        [
            Create("elevation_airtime", true, state => state.ShowElevationAirtime, (state, value) => state with { ShowElevationAirtime = value }),
            Create("elevation_analysis_selection", false, state => state.ShowElevationAnalysisSelection, (state, value) => state with { ShowElevationAnalysisSelection = value }),
        ];

        SignalRowAction Create(string id, bool airtime, Func<RecordedSignalPresentationState, bool> read,
            Func<RecordedSignalPresentationState, bool, RecordedSignalPresentationState> update)
        {
            var action = new SignalRowAction
            {
                Id = id,
                Kind = SignalRowActionKind.Toggle,
                IconPathData = airtime
                    ? "M12 4C7 4 3 7 1 12C3 17 7 20 12 20C17 20 21 17 23 12C21 7 17 4 12 4ZM12 16C9.8 16 8 14.2 8 12C8 9.8 9.8 8 12 8C14.2 8 16 9.8 16 12C16 14.2 14.2 16 12 16Z"
                    : "M4 6H20V8H4V6ZM4 11H17V13H4V11ZM4 16H13V18H4V16Z",
                Command = new RelayCommand(() =>
                {
                    var current = getSignals();
                    actions.SetSignalPresentation(update(current, !read(current)));
                }),
                Tone = SignalRowActionTone.Default,
            };
            bindings.Add((action, read, airtime));
            return action;
        }
    }

    public void ApplyActionStates(RecordedSessionEditorState state)
    {
        var hasSelection = state.AnalysisSelection.HighlightRanges.Count > 0;
        foreach (var (action, read, airtime) in bindings)
        {
            var isChecked = read(state.Presentation.Signals);
            action.IsChecked = isChecked;
            action.IsVisible = !airtime || state.TelemetryData?.Airtimes.Length > 0;
            action.IsEnabled = airtime || hasSelection;
            action.ToolTip = airtime
                ? isChecked ? "Hide airtime" : "Show airtime"
                : hasSelection
                    ? isChecked ? "Hide analysis selection" : "Show analysis selection"
                    : "Select a bin to highlight matching signal spans.";
        }
    }
}
