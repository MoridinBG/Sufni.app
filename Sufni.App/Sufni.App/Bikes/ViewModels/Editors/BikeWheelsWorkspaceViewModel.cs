using System;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Bikes.Models;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.ViewModels.Editors;

public sealed record RimSizeOption(EtrtoRimSize Value, string DisplayName);

public sealed partial class BikeWheelsWorkspaceViewModel(BikeEditorStateController controller) : BikeEditorWorkspace(controller)
{
    public RimSizeOption[] RimSizeOptions { get; } = Enum.GetValues<EtrtoRimSize>().Select(rim => new RimSizeOption(rim, rim.DisplayName)).ToArray();
    public bool IsWheelEditingAvailable => State.Inputs.Mode == RearSuspensionKind.Linkage && State.Inputs.ShockStroke.HasValue;
    public bool HasWheels => State.Geometry.HasWheels;
    public EtrtoRimSize? FrontWheelRimSize
    {
        get => State.Inputs.FrontWheel?.RimSize;
        set { if (value != FrontWheelRimSize) Actions.WheelRim(true, value); }
    }
    public double? FrontWheelTireWidth
    {
        get => State.Inputs.FrontWheel?.TireWidth;
        set { if (value != FrontWheelTireWidth) Actions.WheelWidth(true, value); }
    }
    public double? FrontWheelDiameter
    {
        get => State.Inputs.FrontWheel?.DiameterMm;
        set { if (value != FrontWheelDiameter) Actions.WheelDiameter(true, value); }
    }
    public double FrontWheelCircleLeft => State.Geometry.Front.Left;
    public double FrontWheelCircleTop => State.Geometry.Front.Top;
    public double FrontWheelCircleDiameter => State.Geometry.Front.Diameter;
    public double FrontRimCircleDiameter => State.Geometry.Front.RimDiameter;
    public double FrontTireThickness => State.Geometry.Front.TireThickness;
    public double FrontHubDiameter => State.Geometry.Front.HubDiameter;
    public string FrontWheelDisplayText => FormatWheelDisplay(FrontWheelRimSize, FrontWheelTireWidth, FrontWheelDiameter);
    private bool CanRemoveFrontWheel() => State.Inputs.FrontWheel is not null;
    [RelayCommand(CanExecute = nameof(CanRemoveFrontWheel))]
    private void RemoveFrontWheel() => Actions.RemoveWheel(true);

    public EtrtoRimSize? RearWheelRimSize
    {
        get => State.Inputs.RearWheel?.RimSize;
        set { if (value != RearWheelRimSize) Actions.WheelRim(false, value); }
    }
    public double? RearWheelTireWidth
    {
        get => State.Inputs.RearWheel?.TireWidth;
        set { if (value != RearWheelTireWidth) Actions.WheelWidth(false, value); }
    }
    public double? RearWheelDiameter
    {
        get => State.Inputs.RearWheel?.DiameterMm;
        set { if (value != RearWheelDiameter) Actions.WheelDiameter(false, value); }
    }
    public double RearWheelCircleLeft => State.Geometry.Rear.Left;
    public double RearWheelCircleTop => State.Geometry.Rear.Top;
    public double RearWheelCircleDiameter => State.Geometry.Rear.Diameter;
    public double RearRimCircleDiameter => State.Geometry.Rear.RimDiameter;
    public double RearTireThickness => State.Geometry.Rear.TireThickness;
    public double RearHubDiameter => State.Geometry.Rear.HubDiameter;
    public string RearWheelDisplayText => FormatWheelDisplay(RearWheelRimSize, RearWheelTireWidth, RearWheelDiameter);
    private bool CanRemoveRearWheel() => State.Inputs.RearWheel is not null;
    [RelayCommand(CanExecute = nameof(CanRemoveRearWheel))]
    private void RemoveRearWheel() => Actions.RemoveWheel(false);

    private static string FormatWheelDisplay(EtrtoRimSize? rimSize, double? tireWidth, double? diameter)
    {
        if (!diameter.HasValue) return "";

        if (rimSize.HasValue && tireWidth.HasValue)
        {
            return $"{rimSize.Value.DisplayName} / {tireWidth:0.00}\"";
        }

        return $"{diameter:0.0} mm";
    }

    internal override void Apply(BikeEditorState state)
    {
        base.Apply(state);
        RemoveFrontWheelCommand.NotifyCanExecuteChanged();
        RemoveRearWheelCommand.NotifyCanExecuteChanged();
    }
}
