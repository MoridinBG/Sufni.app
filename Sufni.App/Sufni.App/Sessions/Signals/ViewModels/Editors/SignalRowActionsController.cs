using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using System.Collections.Generic;
using System;

namespace Sufni.App.Sessions.Signals.ViewModels.Editors;

/// <summary>
/// Owns the per-row plot header actions for a recorded session: the six
/// airtime toggles and six analysis-selection toggles, their icon and
/// tool-tip state.
/// </summary>
internal sealed class SignalRowActionsController
{
    private readonly Func<bool> hasAnalysisSelection;
    private readonly Func<bool> showAirtime;
    private readonly Action<bool> setShowAirtime;
    private readonly Func<bool> showVelocityAirtime;
    private readonly Action<bool> setShowVelocityAirtime;
    private readonly Func<bool> showImuAirtime;
    private readonly Action<bool> setShowImuAirtime;
    private readonly Func<bool> showPitchRollAirtime;
    private readonly Action<bool> setShowPitchRollAirtime;
    private readonly Func<bool> showSpeedAirtime;
    private readonly Action<bool> setShowSpeedAirtime;
    private readonly Func<bool> showElevationAirtime;
    private readonly Action<bool> setShowElevationAirtime;
    private readonly Func<bool> showAnalysisSelection;
    private readonly Action<bool> setShowAnalysisSelection;
    private readonly Func<bool> showVelocityAnalysisSelection;
    private readonly Action<bool> setShowVelocityAnalysisSelection;
    private readonly Func<bool> showImuAnalysisSelection;
    private readonly Action<bool> setShowImuAnalysisSelection;
    private readonly Func<bool> showPitchRollAnalysisSelection;
    private readonly Action<bool> setShowPitchRollAnalysisSelection;
    private readonly Func<bool> showSpeedAnalysisSelection;
    private readonly Action<bool> setShowSpeedAnalysisSelection;
    private readonly Func<bool> showElevationAnalysisSelection;
    private readonly Action<bool> setShowElevationAnalysisSelection;
    private readonly SignalRowAction showAirtimeAction;
    private readonly SignalRowAction showVelocityAirtimeAction;
    private readonly SignalRowAction showImuAirtimeAction;
    private readonly SignalRowAction showPitchRollAirtimeAction;
    private readonly SignalRowAction showSpeedAirtimeAction;
    private readonly SignalRowAction showElevationAirtimeAction;
    private readonly SignalRowAction showAnalysisSelectionAction;
    private readonly SignalRowAction showVelocityAnalysisSelectionAction;
    private readonly SignalRowAction showImuAnalysisSelectionAction;
    private readonly SignalRowAction showPitchRollAnalysisSelectionAction;
    private readonly SignalRowAction showSpeedAnalysisSelectionAction;
    private readonly SignalRowAction showElevationAnalysisSelectionAction;

    public IReadOnlyList<SignalRowAction> TravelHeaderActions { get; }
    public IReadOnlyList<SignalRowAction> VelocityHeaderActions { get; }
    public IReadOnlyList<SignalRowAction> ImuHeaderActions { get; }
    public IReadOnlyList<SignalRowAction> PitchRollHeaderActions { get; }
    public IReadOnlyList<SignalRowAction> SpeedHeaderActions { get; }
    public IReadOnlyList<SignalRowAction> ElevationHeaderActions { get; }

    public SignalRowActionsController(
        Func<bool> hasAnalysisSelection,
        Func<bool> showAirtime,
        Action<bool> setShowAirtime,
        Func<bool> showVelocityAirtime,
        Action<bool> setShowVelocityAirtime,
        Func<bool> showImuAirtime,
        Action<bool> setShowImuAirtime,
        Func<bool> showPitchRollAirtime,
        Action<bool> setShowPitchRollAirtime,
        Func<bool> showSpeedAirtime,
        Action<bool> setShowSpeedAirtime,
        Func<bool> showElevationAirtime,
        Action<bool> setShowElevationAirtime,
        Func<bool> showAnalysisSelection,
        Action<bool> setShowAnalysisSelection,
        Func<bool> showVelocityAnalysisSelection,
        Action<bool> setShowVelocityAnalysisSelection,
        Func<bool> showImuAnalysisSelection,
        Action<bool> setShowImuAnalysisSelection,
        Func<bool> showPitchRollAnalysisSelection,
        Action<bool> setShowPitchRollAnalysisSelection,
        Func<bool> showSpeedAnalysisSelection,
        Action<bool> setShowSpeedAnalysisSelection,
        Func<bool> showElevationAnalysisSelection,
        Action<bool> setShowElevationAnalysisSelection)
    {
        this.hasAnalysisSelection = hasAnalysisSelection;
        this.showAirtime = showAirtime;
        this.setShowAirtime = setShowAirtime;
        this.showVelocityAirtime = showVelocityAirtime;
        this.setShowVelocityAirtime = setShowVelocityAirtime;
        this.showImuAirtime = showImuAirtime;
        this.setShowImuAirtime = setShowImuAirtime;
        this.showPitchRollAirtime = showPitchRollAirtime;
        this.setShowPitchRollAirtime = setShowPitchRollAirtime;
        this.showSpeedAirtime = showSpeedAirtime;
        this.setShowSpeedAirtime = setShowSpeedAirtime;
        this.showElevationAirtime = showElevationAirtime;
        this.setShowElevationAirtime = setShowElevationAirtime;
        this.showAnalysisSelection = showAnalysisSelection;
        this.setShowAnalysisSelection = setShowAnalysisSelection;
        this.showVelocityAnalysisSelection = showVelocityAnalysisSelection;
        this.setShowVelocityAnalysisSelection = setShowVelocityAnalysisSelection;
        this.showImuAnalysisSelection = showImuAnalysisSelection;
        this.setShowImuAnalysisSelection = setShowImuAnalysisSelection;
        this.showPitchRollAnalysisSelection = showPitchRollAnalysisSelection;
        this.setShowPitchRollAnalysisSelection = setShowPitchRollAnalysisSelection;
        this.showSpeedAnalysisSelection = showSpeedAnalysisSelection;
        this.setShowSpeedAnalysisSelection = setShowSpeedAnalysisSelection;
        this.showElevationAnalysisSelection = showElevationAnalysisSelection;
        this.setShowElevationAnalysisSelection = setShowElevationAnalysisSelection;

        showAirtimeAction = CreateBoundAirtimeAction("travel_airtime", showAirtime, setShowAirtime);
        showVelocityAirtimeAction = CreateBoundAirtimeAction("velocity_airtime", showVelocityAirtime, setShowVelocityAirtime);
        showImuAirtimeAction = CreateBoundAirtimeAction("imu_airtime", showImuAirtime, setShowImuAirtime);
        showPitchRollAirtimeAction = CreateBoundAirtimeAction("pitch_roll_airtime", showPitchRollAirtime, setShowPitchRollAirtime);
        showSpeedAirtimeAction = CreateBoundAirtimeAction("speed_airtime", showSpeedAirtime, setShowSpeedAirtime);
        showElevationAirtimeAction = CreateBoundAirtimeAction("elevation_airtime", showElevationAirtime, setShowElevationAirtime);
        showAnalysisSelectionAction = CreateBoundAnalysisSelectionAction("travel_analysis_selection", showAnalysisSelection, setShowAnalysisSelection);
        showVelocityAnalysisSelectionAction = CreateBoundAnalysisSelectionAction("velocity_analysis_selection", showVelocityAnalysisSelection, setShowVelocityAnalysisSelection);
        showImuAnalysisSelectionAction = CreateBoundAnalysisSelectionAction("imu_analysis_selection", showImuAnalysisSelection, setShowImuAnalysisSelection);
        showPitchRollAnalysisSelectionAction = CreateBoundAnalysisSelectionAction("pitch_roll_analysis_selection", showPitchRollAnalysisSelection, setShowPitchRollAnalysisSelection);
        showSpeedAnalysisSelectionAction = CreateBoundAnalysisSelectionAction("speed_analysis_selection", showSpeedAnalysisSelection, setShowSpeedAnalysisSelection);
        showElevationAnalysisSelectionAction = CreateBoundAnalysisSelectionAction("elevation_analysis_selection", showElevationAnalysisSelection, setShowElevationAnalysisSelection);
        TravelHeaderActions = [showAirtimeAction, showAnalysisSelectionAction];
        VelocityHeaderActions = [showVelocityAirtimeAction, showVelocityAnalysisSelectionAction];
        ImuHeaderActions = [showImuAirtimeAction, showImuAnalysisSelectionAction];
        PitchRollHeaderActions = [showPitchRollAirtimeAction, showPitchRollAnalysisSelectionAction];
        SpeedHeaderActions = [showSpeedAirtimeAction, showSpeedAnalysisSelectionAction];
        ElevationHeaderActions = [showElevationAirtimeAction, showElevationAnalysisSelectionAction];
    }

    public void RefreshAnalysisSelectionActionStates()
    {
        var hasSelection = hasAnalysisSelection();
        UpdateAnalysisSelectionAction(showAnalysisSelectionAction, showAnalysisSelection(), hasSelection);
        UpdateAnalysisSelectionAction(showVelocityAnalysisSelectionAction, showVelocityAnalysisSelection(), hasSelection);
        UpdateAnalysisSelectionAction(showImuAnalysisSelectionAction, showImuAnalysisSelection(), hasSelection);
        UpdateAnalysisSelectionAction(showPitchRollAnalysisSelectionAction, showPitchRollAnalysisSelection(), hasSelection);
        UpdateAnalysisSelectionAction(showSpeedAnalysisSelectionAction, showSpeedAnalysisSelection(), hasSelection);
        UpdateAnalysisSelectionAction(showElevationAnalysisSelectionAction, showElevationAnalysisSelection(), hasSelection);
    }

    public void ClearAnalysisSelectionToggles()
    {
        SetAnalysisSelection(showAnalysisSelectionAction, setShowAnalysisSelection, false);
        SetAnalysisSelection(showVelocityAnalysisSelectionAction, setShowVelocityAnalysisSelection, false);
        SetAnalysisSelection(showImuAnalysisSelectionAction, setShowImuAnalysisSelection, false);
        SetAnalysisSelection(showPitchRollAnalysisSelectionAction, setShowPitchRollAnalysisSelection, false);
        SetAnalysisSelection(showSpeedAnalysisSelectionAction, setShowSpeedAnalysisSelection, false);
        SetAnalysisSelection(showElevationAnalysisSelectionAction, setShowElevationAnalysisSelection, false);
    }

    private void ToggleAirtime(Func<bool> get, Action<bool> set, SignalRowAction action)
    {
        var value = !get();
        set(value);
        UpdateAirtimeAction(action, value);
    }

    private void ToggleAnalysisSelection(Func<bool> get, Action<bool> set, SignalRowAction action)
    {
        SetAnalysisSelection(action, set, !get());
    }

    private void SetAnalysisSelection(SignalRowAction action, Action<bool> set, bool value)
    {
        set(value);
        UpdateAnalysisSelectionAction(action, value, hasAnalysisSelection());
    }

    private SignalRowAction CreateBoundAirtimeAction(string id, Func<bool> get, Action<bool> set)
    {
        SignalRowAction action = null!;
        action = CreateAirtimeAction(id, get(), () => ToggleAirtime(get, set, action));
        return action;
    }

    private SignalRowAction CreateBoundAnalysisSelectionAction(string id, Func<bool> get, Action<bool> set)
    {
        SignalRowAction action = null!;
        action = CreateAnalysisSelectionAction(id, get(), () => ToggleAnalysisSelection(get, set, action));
        return action;
    }

    private static SignalRowAction CreateAirtimeAction(string id, bool isChecked, Action toggle)
    {
        return new SignalRowAction
        {
            Id = id,
            Kind = SignalRowActionKind.Toggle,
            IconPathData =
                "M12 4C7 4 3 7 1 12C3 17 7 20 12 20C17 20 21 17 23 12C21 7 17 4 12 4ZM12 16C9.8 16 8 14.2 8 12C8 9.8 9.8 8 12 8C14.2 8 16 9.8 16 12C16 14.2 14.2 16 12 16Z",
            ToolTip = isChecked ? "Hide airtime" : "Show airtime",
            Command = new RelayCommand(toggle),
            IsChecked = isChecked,
            Tone = SignalRowActionTone.Default,
        };
    }

    private SignalRowAction CreateAnalysisSelectionAction(string id, bool isChecked, Action toggle)
    {
        var action = new SignalRowAction
        {
            Id = id,
            Kind = SignalRowActionKind.Toggle,
            IconPathData = "M4 6H20V8H4V6ZM4 11H17V13H4V11ZM4 16H13V18H4V16Z",
            Command = new RelayCommand(toggle),
            Tone = SignalRowActionTone.Default,
        };
        UpdateAnalysisSelectionAction(action, isChecked, hasAnalysisSelection());
        return action;
    }

    private static void UpdateAirtimeAction(SignalRowAction action, bool isChecked)
    {
        action.IsChecked = isChecked;
        action.ToolTip = isChecked ? "Hide airtime" : "Show airtime";
    }

    private static void UpdateAnalysisSelectionAction(SignalRowAction action, bool isChecked, bool hasSelection)
    {
        action.IsChecked = isChecked;
        action.IsEnabled = hasSelection;
        action.ToolTip = hasSelection
            ? isChecked ? "Hide analysis selection" : "Show analysis selection"
            : "Select a bin to highlight matching signal spans.";
    }
}
