using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using System.Collections.Generic;
using System.ComponentModel;
using System;

using Sufni.App.Sessions.Detail.ViewModels.Editors;
namespace Sufni.App.Sessions.Signals.ViewModels.Editors;

/// <summary>
/// Owns the per-row plot header actions for a recorded session: the six
/// airtime toggles and six analysis-selection toggles, their icon and
/// tool-tip state, and the context subscription that keeps them in sync
/// with the Show* properties.
/// </summary>
internal sealed class SignalRowActionsController
{
    private readonly RecordedSessionContext context;
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

    public SignalRowActionsController(RecordedSessionContext context)
    {
        this.context = context;
        showAirtimeAction = CreateAirtimeAction("travel_airtime", context.ShowAirtime, () => context.ShowAirtime = !context.ShowAirtime);
        showVelocityAirtimeAction = CreateAirtimeAction("velocity_airtime", context.ShowVelocityAirtime, () => context.ShowVelocityAirtime = !context.ShowVelocityAirtime);
        showImuAirtimeAction = CreateAirtimeAction("imu_airtime", context.ShowImuAirtime, () => context.ShowImuAirtime = !context.ShowImuAirtime);
        showPitchRollAirtimeAction = CreateAirtimeAction("pitch_roll_airtime", context.ShowPitchRollAirtime, () => context.ShowPitchRollAirtime = !context.ShowPitchRollAirtime);
        showSpeedAirtimeAction = CreateAirtimeAction("speed_airtime", context.ShowSpeedAirtime, () => context.ShowSpeedAirtime = !context.ShowSpeedAirtime);
        showElevationAirtimeAction = CreateAirtimeAction("elevation_airtime", context.ShowElevationAirtime, () => context.ShowElevationAirtime = !context.ShowElevationAirtime);
        showAnalysisSelectionAction = CreateAnalysisSelectionAction("travel_analysis_selection", context.ShowAnalysisSelection, () => context.ShowAnalysisSelection = !context.ShowAnalysisSelection);
        showVelocityAnalysisSelectionAction = CreateAnalysisSelectionAction("velocity_analysis_selection", context.ShowVelocityAnalysisSelection, () => context.ShowVelocityAnalysisSelection = !context.ShowVelocityAnalysisSelection);
        showImuAnalysisSelectionAction = CreateAnalysisSelectionAction("imu_analysis_selection", context.ShowImuAnalysisSelection, () => context.ShowImuAnalysisSelection = !context.ShowImuAnalysisSelection);
        showPitchRollAnalysisSelectionAction = CreateAnalysisSelectionAction("pitch_roll_analysis_selection", context.ShowPitchRollAnalysisSelection, () => context.ShowPitchRollAnalysisSelection = !context.ShowPitchRollAnalysisSelection);
        showSpeedAnalysisSelectionAction = CreateAnalysisSelectionAction("speed_analysis_selection", context.ShowSpeedAnalysisSelection, () => context.ShowSpeedAnalysisSelection = !context.ShowSpeedAnalysisSelection);
        showElevationAnalysisSelectionAction = CreateAnalysisSelectionAction("elevation_analysis_selection", context.ShowElevationAnalysisSelection, () => context.ShowElevationAnalysisSelection = !context.ShowElevationAnalysisSelection);
        TravelHeaderActions = [showAirtimeAction, showAnalysisSelectionAction];
        VelocityHeaderActions = [showVelocityAirtimeAction, showVelocityAnalysisSelectionAction];
        ImuHeaderActions = [showImuAirtimeAction, showImuAnalysisSelectionAction];
        PitchRollHeaderActions = [showPitchRollAirtimeAction, showPitchRollAnalysisSelectionAction];
        SpeedHeaderActions = [showSpeedAirtimeAction, showSpeedAnalysisSelectionAction];
        ElevationHeaderActions = [showElevationAirtimeAction, showElevationAnalysisSelectionAction];
        context.TravelHeaderActions = TravelHeaderActions;
        context.VelocityHeaderActions = VelocityHeaderActions;
        context.ImuHeaderActions = ImuHeaderActions;
        context.PitchRollHeaderActions = PitchRollHeaderActions;
        context.SpeedHeaderActions = SpeedHeaderActions;
        context.ElevationHeaderActions = ElevationHeaderActions;
        context.PropertyChanged += OnContextPropertyChanged;
    }

    public void RefreshAnalysisSelectionActionStates()
    {
        var hasSelection = context.HasAnalysisSelection;
        UpdateAnalysisSelectionAction(showAnalysisSelectionAction, context.ShowAnalysisSelection, hasSelection);
        UpdateAnalysisSelectionAction(showVelocityAnalysisSelectionAction, context.ShowVelocityAnalysisSelection, hasSelection);
        UpdateAnalysisSelectionAction(showImuAnalysisSelectionAction, context.ShowImuAnalysisSelection, hasSelection);
        UpdateAnalysisSelectionAction(showPitchRollAnalysisSelectionAction, context.ShowPitchRollAnalysisSelection, hasSelection);
        UpdateAnalysisSelectionAction(showSpeedAnalysisSelectionAction, context.ShowSpeedAnalysisSelection, hasSelection);
        UpdateAnalysisSelectionAction(showElevationAnalysisSelectionAction, context.ShowElevationAnalysisSelection, hasSelection);
    }

    public void ClearAnalysisSelectionToggles()
    {
        context.ShowAnalysisSelection = false;
        context.ShowVelocityAnalysisSelection = false;
        context.ShowImuAnalysisSelection = false;
        context.ShowPitchRollAnalysisSelection = false;
        context.ShowSpeedAnalysisSelection = false;
        context.ShowElevationAnalysisSelection = false;
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(RecordedSessionContext.ShowAirtime):
                UpdateAirtimeAction(showAirtimeAction, context.ShowAirtime);
                break;
            case nameof(RecordedSessionContext.ShowVelocityAirtime):
                UpdateAirtimeAction(showVelocityAirtimeAction, context.ShowVelocityAirtime);
                break;
            case nameof(RecordedSessionContext.ShowImuAirtime):
                UpdateAirtimeAction(showImuAirtimeAction, context.ShowImuAirtime);
                break;
            case nameof(RecordedSessionContext.ShowPitchRollAirtime):
                UpdateAirtimeAction(showPitchRollAirtimeAction, context.ShowPitchRollAirtime);
                break;
            case nameof(RecordedSessionContext.ShowSpeedAirtime):
                UpdateAirtimeAction(showSpeedAirtimeAction, context.ShowSpeedAirtime);
                break;
            case nameof(RecordedSessionContext.ShowElevationAirtime):
                UpdateAirtimeAction(showElevationAirtimeAction, context.ShowElevationAirtime);
                break;
            case nameof(RecordedSessionContext.ShowAnalysisSelection):
                UpdateAnalysisSelectionAction(showAnalysisSelectionAction, context.ShowAnalysisSelection, context.HasAnalysisSelection);
                break;
            case nameof(RecordedSessionContext.ShowVelocityAnalysisSelection):
                UpdateAnalysisSelectionAction(showVelocityAnalysisSelectionAction, context.ShowVelocityAnalysisSelection, context.HasAnalysisSelection);
                break;
            case nameof(RecordedSessionContext.ShowImuAnalysisSelection):
                UpdateAnalysisSelectionAction(showImuAnalysisSelectionAction, context.ShowImuAnalysisSelection, context.HasAnalysisSelection);
                break;
            case nameof(RecordedSessionContext.ShowPitchRollAnalysisSelection):
                UpdateAnalysisSelectionAction(showPitchRollAnalysisSelectionAction, context.ShowPitchRollAnalysisSelection, context.HasAnalysisSelection);
                break;
            case nameof(RecordedSessionContext.ShowSpeedAnalysisSelection):
                UpdateAnalysisSelectionAction(showSpeedAnalysisSelectionAction, context.ShowSpeedAnalysisSelection, context.HasAnalysisSelection);
                break;
            case nameof(RecordedSessionContext.ShowElevationAnalysisSelection):
                UpdateAnalysisSelectionAction(showElevationAnalysisSelectionAction, context.ShowElevationAnalysisSelection, context.HasAnalysisSelection);
                break;
        }
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
        UpdateAnalysisSelectionAction(action, isChecked, context.HasAnalysisSelection);
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
