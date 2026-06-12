using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using System.Collections.Generic;
using System.ComponentModel;
using System;

namespace Sufni.App.ViewModels.Editors;

/// <summary>
/// Owns the per-row plot header actions for a recorded session: the six
/// airtime toggles and six statistics-selection toggles, their icon and
/// tool-tip state, and the context subscription that keeps them in sync
/// with the Show* properties.
/// </summary>
internal sealed class SessionPlotRowActionsController
{
    private readonly RecordedSessionContext context;
    private readonly TelemetryPlotRowAction showAirtimeAction;
    private readonly TelemetryPlotRowAction showVelocityAirtimeAction;
    private readonly TelemetryPlotRowAction showImuAirtimeAction;
    private readonly TelemetryPlotRowAction showPitchRollAirtimeAction;
    private readonly TelemetryPlotRowAction showSpeedAirtimeAction;
    private readonly TelemetryPlotRowAction showElevationAirtimeAction;
    private readonly TelemetryPlotRowAction showStatisticsSelectionAction;
    private readonly TelemetryPlotRowAction showVelocityStatisticsSelectionAction;
    private readonly TelemetryPlotRowAction showImuStatisticsSelectionAction;
    private readonly TelemetryPlotRowAction showPitchRollStatisticsSelectionAction;
    private readonly TelemetryPlotRowAction showSpeedStatisticsSelectionAction;
    private readonly TelemetryPlotRowAction showElevationStatisticsSelectionAction;

    public IReadOnlyList<TelemetryPlotRowAction> TravelHeaderActions { get; }
    public IReadOnlyList<TelemetryPlotRowAction> VelocityHeaderActions { get; }
    public IReadOnlyList<TelemetryPlotRowAction> ImuHeaderActions { get; }
    public IReadOnlyList<TelemetryPlotRowAction> PitchRollHeaderActions { get; }
    public IReadOnlyList<TelemetryPlotRowAction> SpeedHeaderActions { get; }
    public IReadOnlyList<TelemetryPlotRowAction> ElevationHeaderActions { get; }

    public SessionPlotRowActionsController(RecordedSessionContext context)
    {
        this.context = context;
        showAirtimeAction = CreateAirtimeAction("travel_airtime", context.ShowAirtime, () => context.ShowAirtime = !context.ShowAirtime);
        showVelocityAirtimeAction = CreateAirtimeAction("velocity_airtime", context.ShowVelocityAirtime, () => context.ShowVelocityAirtime = !context.ShowVelocityAirtime);
        showImuAirtimeAction = CreateAirtimeAction("imu_airtime", context.ShowImuAirtime, () => context.ShowImuAirtime = !context.ShowImuAirtime);
        showPitchRollAirtimeAction = CreateAirtimeAction("pitch_roll_airtime", context.ShowPitchRollAirtime, () => context.ShowPitchRollAirtime = !context.ShowPitchRollAirtime);
        showSpeedAirtimeAction = CreateAirtimeAction("speed_airtime", context.ShowSpeedAirtime, () => context.ShowSpeedAirtime = !context.ShowSpeedAirtime);
        showElevationAirtimeAction = CreateAirtimeAction("elevation_airtime", context.ShowElevationAirtime, () => context.ShowElevationAirtime = !context.ShowElevationAirtime);
        showStatisticsSelectionAction = CreateStatisticsSelectionAction("travel_statistics_selection", context.ShowStatisticsSelection, () => context.ShowStatisticsSelection = !context.ShowStatisticsSelection);
        showVelocityStatisticsSelectionAction = CreateStatisticsSelectionAction("velocity_statistics_selection", context.ShowVelocityStatisticsSelection, () => context.ShowVelocityStatisticsSelection = !context.ShowVelocityStatisticsSelection);
        showImuStatisticsSelectionAction = CreateStatisticsSelectionAction("imu_statistics_selection", context.ShowImuStatisticsSelection, () => context.ShowImuStatisticsSelection = !context.ShowImuStatisticsSelection);
        showPitchRollStatisticsSelectionAction = CreateStatisticsSelectionAction("pitch_roll_statistics_selection", context.ShowPitchRollStatisticsSelection, () => context.ShowPitchRollStatisticsSelection = !context.ShowPitchRollStatisticsSelection);
        showSpeedStatisticsSelectionAction = CreateStatisticsSelectionAction("speed_statistics_selection", context.ShowSpeedStatisticsSelection, () => context.ShowSpeedStatisticsSelection = !context.ShowSpeedStatisticsSelection);
        showElevationStatisticsSelectionAction = CreateStatisticsSelectionAction("elevation_statistics_selection", context.ShowElevationStatisticsSelection, () => context.ShowElevationStatisticsSelection = !context.ShowElevationStatisticsSelection);
        TravelHeaderActions = [showAirtimeAction, showStatisticsSelectionAction];
        VelocityHeaderActions = [showVelocityAirtimeAction, showVelocityStatisticsSelectionAction];
        ImuHeaderActions = [showImuAirtimeAction, showImuStatisticsSelectionAction];
        PitchRollHeaderActions = [showPitchRollAirtimeAction, showPitchRollStatisticsSelectionAction];
        SpeedHeaderActions = [showSpeedAirtimeAction, showSpeedStatisticsSelectionAction];
        ElevationHeaderActions = [showElevationAirtimeAction, showElevationStatisticsSelectionAction];
        context.TravelHeaderActions = TravelHeaderActions;
        context.VelocityHeaderActions = VelocityHeaderActions;
        context.ImuHeaderActions = ImuHeaderActions;
        context.PitchRollHeaderActions = PitchRollHeaderActions;
        context.SpeedHeaderActions = SpeedHeaderActions;
        context.ElevationHeaderActions = ElevationHeaderActions;
        context.PropertyChanged += OnContextPropertyChanged;
    }

    public void RefreshStatisticsSelectionActionStates()
    {
        var hasSelection = context.HasStatisticsSelection;
        UpdateStatisticsSelectionAction(showStatisticsSelectionAction, context.ShowStatisticsSelection, hasSelection);
        UpdateStatisticsSelectionAction(showVelocityStatisticsSelectionAction, context.ShowVelocityStatisticsSelection, hasSelection);
        UpdateStatisticsSelectionAction(showImuStatisticsSelectionAction, context.ShowImuStatisticsSelection, hasSelection);
        UpdateStatisticsSelectionAction(showPitchRollStatisticsSelectionAction, context.ShowPitchRollStatisticsSelection, hasSelection);
        UpdateStatisticsSelectionAction(showSpeedStatisticsSelectionAction, context.ShowSpeedStatisticsSelection, hasSelection);
        UpdateStatisticsSelectionAction(showElevationStatisticsSelectionAction, context.ShowElevationStatisticsSelection, hasSelection);
    }

    public void ClearStatisticsSelectionToggles()
    {
        context.ShowStatisticsSelection = false;
        context.ShowVelocityStatisticsSelection = false;
        context.ShowImuStatisticsSelection = false;
        context.ShowPitchRollStatisticsSelection = false;
        context.ShowSpeedStatisticsSelection = false;
        context.ShowElevationStatisticsSelection = false;
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
            case nameof(RecordedSessionContext.ShowStatisticsSelection):
                UpdateStatisticsSelectionAction(showStatisticsSelectionAction, context.ShowStatisticsSelection, context.HasStatisticsSelection);
                break;
            case nameof(RecordedSessionContext.ShowVelocityStatisticsSelection):
                UpdateStatisticsSelectionAction(showVelocityStatisticsSelectionAction, context.ShowVelocityStatisticsSelection, context.HasStatisticsSelection);
                break;
            case nameof(RecordedSessionContext.ShowImuStatisticsSelection):
                UpdateStatisticsSelectionAction(showImuStatisticsSelectionAction, context.ShowImuStatisticsSelection, context.HasStatisticsSelection);
                break;
            case nameof(RecordedSessionContext.ShowPitchRollStatisticsSelection):
                UpdateStatisticsSelectionAction(showPitchRollStatisticsSelectionAction, context.ShowPitchRollStatisticsSelection, context.HasStatisticsSelection);
                break;
            case nameof(RecordedSessionContext.ShowSpeedStatisticsSelection):
                UpdateStatisticsSelectionAction(showSpeedStatisticsSelectionAction, context.ShowSpeedStatisticsSelection, context.HasStatisticsSelection);
                break;
            case nameof(RecordedSessionContext.ShowElevationStatisticsSelection):
                UpdateStatisticsSelectionAction(showElevationStatisticsSelectionAction, context.ShowElevationStatisticsSelection, context.HasStatisticsSelection);
                break;
        }
    }

    private static TelemetryPlotRowAction CreateAirtimeAction(string id, bool isChecked, Action toggle)
    {
        return new TelemetryPlotRowAction
        {
            Id = id,
            Kind = TelemetryPlotRowActionKind.Toggle,
            IconPathData =
                "M12 4C7 4 3 7 1 12C3 17 7 20 12 20C17 20 21 17 23 12C21 7 17 4 12 4ZM12 16C9.8 16 8 14.2 8 12C8 9.8 9.8 8 12 8C14.2 8 16 9.8 16 12C16 14.2 14.2 16 12 16Z",
            ToolTip = isChecked ? "Hide airtime" : "Show airtime",
            Command = new RelayCommand(toggle),
            IsChecked = isChecked,
            Tone = TelemetryPlotRowActionTone.Default,
        };
    }

    private TelemetryPlotRowAction CreateStatisticsSelectionAction(string id, bool isChecked, Action toggle)
    {
        var action = new TelemetryPlotRowAction
        {
            Id = id,
            Kind = TelemetryPlotRowActionKind.Toggle,
            IconPathData = "M4 6H20V8H4V6ZM4 11H17V13H4V11ZM4 16H13V18H4V16Z",
            Command = new RelayCommand(toggle),
            Tone = TelemetryPlotRowActionTone.Default,
        };
        UpdateStatisticsSelectionAction(action, isChecked, context.HasStatisticsSelection);
        return action;
    }

    private static void UpdateAirtimeAction(TelemetryPlotRowAction action, bool isChecked)
    {
        action.IsChecked = isChecked;
        action.ToolTip = isChecked ? "Hide airtime" : "Show airtime";
    }

    private static void UpdateStatisticsSelectionAction(TelemetryPlotRowAction action, bool isChecked, bool hasSelection)
    {
        action.IsChecked = isChecked;
        action.IsEnabled = hasSelection;
        action.ToolTip = hasSelection
            ? isChecked ? "Hide selected strokes" : "Show selected strokes"
            : "Select a stroke group";
    }
}
