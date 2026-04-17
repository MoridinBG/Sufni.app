using System;
using System.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Stores;
using Sufni.Kinematics;

namespace Sufni.App.ViewModels.Editors;

public partial class BikeWheelGeometryViewModel : ObservableObject
{
    private bool suppressWheelStateCallbacks;
    private Point? frontWheelCenter;
    private Point? rearWheelCenter;
    private double? pixelsToMillimeters;

    public RimSizeOption[] RimSizeOptions { get; } = Enum.GetValues<EtrtoRimSize>()
        .Select(rimSize => new RimSizeOption(rimSize, rimSize.DisplayName))
        .ToArray();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveFrontWheelCommand))]
    private EtrtoRimSize? frontWheelRimSize;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveFrontWheelCommand))]
    private double? frontWheelTireWidth;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveFrontWheelCommand))]
    private double? frontWheelDiameter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveRearWheelCommand))]
    private EtrtoRimSize? rearWheelRimSize;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveRearWheelCommand))]
    private double? rearWheelTireWidth;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveRearWheelCommand))]
    private double? rearWheelDiameter;

    public bool HasWheels =>
        FrontWheelDiameter.HasValue &&
        RearWheelDiameter.HasValue &&
        frontWheelCenter.HasValue &&
        rearWheelCenter.HasValue;

    public double? FrontWheelRadiusPixels => FrontWheelDiameter / 2.0 / pixelsToMillimeters;
    public double? RearWheelRadiusPixels => RearWheelDiameter / 2.0 / pixelsToMillimeters;

    public double FrontWheelCircleLeft => frontWheelCenter?.X - (FrontWheelRadiusPixels ?? 0) ?? 0;
    public double FrontWheelCircleTop => frontWheelCenter?.Y - (FrontWheelRadiusPixels ?? 0) ?? 0;
    public double FrontWheelCircleDiameter => (FrontWheelRadiusPixels ?? 0) * 2;

    public double RearWheelCircleLeft => rearWheelCenter?.X - (RearWheelRadiusPixels ?? 0) ?? 0;
    public double RearWheelCircleTop => rearWheelCenter?.Y - (RearWheelRadiusPixels ?? 0) ?? 0;
    public double RearWheelCircleDiameter => (RearWheelRadiusPixels ?? 0) * 2;

    public double FrontRimCircleDiameter => ComputeRimCircleDiameter(FrontWheelRimSize, FrontWheelDiameter);
    public double RearRimCircleDiameter => ComputeRimCircleDiameter(RearWheelRimSize, RearWheelDiameter);

    public double FrontTireThickness => (FrontWheelCircleDiameter - FrontRimCircleDiameter) / 2;
    public double RearTireThickness => (RearWheelCircleDiameter - RearRimCircleDiameter) / 2;

    public double FrontHubDiameter => FrontWheelCircleDiameter * 0.08;
    public double RearHubDiameter => RearWheelCircleDiameter * 0.08;

    public string FrontWheelDisplayText => FormatWheelDisplay(FrontWheelRimSize, FrontWheelTireWidth, FrontWheelDiameter);
    public string RearWheelDisplayText => FormatWheelDisplay(RearWheelRimSize, RearWheelTireWidth, RearWheelDiameter);

    partial void OnFrontWheelRimSizeChanged(EtrtoRimSize? value)
    {
        if (suppressWheelStateCallbacks) return;
        RecalculateFrontWheelDiameter();
    }

    partial void OnFrontWheelTireWidthChanged(double? value)
    {
        if (suppressWheelStateCallbacks) return;
        RecalculateFrontWheelDiameter();
    }

    partial void OnFrontWheelDiameterChanged(double? value)
    {
        if (suppressWheelStateCallbacks) return;

        WithWheelStateCallbacksSuspended(() =>
        {
            FrontWheelRimSize = null;
            FrontWheelTireWidth = null;
        });

        NotifyFrontWheelPropertiesChanged();
    }

    partial void OnRearWheelRimSizeChanged(EtrtoRimSize? value)
    {
        if (suppressWheelStateCallbacks) return;
        RecalculateRearWheelDiameter();
    }

    partial void OnRearWheelTireWidthChanged(double? value)
    {
        if (suppressWheelStateCallbacks) return;
        RecalculateRearWheelDiameter();
    }

    partial void OnRearWheelDiameterChanged(double? value)
    {
        if (suppressWheelStateCallbacks) return;

        WithWheelStateCallbacksSuspended(() =>
        {
            RearWheelRimSize = null;
            RearWheelTireWidth = null;
        });

        NotifyRearWheelPropertiesChanged();
    }

    public void ApplySnapshot(BikeSnapshot snapshot)
    {
        WithWheelStateCallbacksSuspended(() =>
        {
            FrontWheelRimSize = snapshot.FrontWheelRimSize;
            FrontWheelTireWidth = snapshot.FrontWheelTireWidth;
            FrontWheelDiameter = snapshot.FrontWheelDiameterMm;
            RearWheelRimSize = snapshot.RearWheelRimSize;
            RearWheelTireWidth = snapshot.RearWheelTireWidth;
            RearWheelDiameter = snapshot.RearWheelDiameterMm;
        });

        NotifyFrontWheelPropertiesChanged();
        NotifyRearWheelPropertiesChanged();
    }

    public void RefreshDerived(Point? frontWheelCenter, Point? rearWheelCenter, double? pixelsToMillimeters)
    {
        this.frontWheelCenter = frontWheelCenter;
        this.rearWheelCenter = rearWheelCenter;
        this.pixelsToMillimeters = pixelsToMillimeters;

        NotifyFrontWheelPropertiesChanged();
        NotifyRearWheelPropertiesChanged();
    }

    public double? TryComputeGroundAlignmentDelta(Point? frontWheelCenter, Point? rearWheelCenter, double? pixelsToMillimeters)
    {
        if (!frontWheelCenter.HasValue ||
            !rearWheelCenter.HasValue ||
            !FrontWheelDiameter.HasValue ||
            !RearWheelDiameter.HasValue ||
            !pixelsToMillimeters.HasValue)
        {
            return null;
        }

        var frontRadiusPx = FrontWheelDiameter.Value / 2.0 / pixelsToMillimeters.Value;
        var rearRadiusPx = RearWheelDiameter.Value / 2.0 / pixelsToMillimeters.Value;

        var (rotationAngle, _) = GroundCalculator.CalculateGroundRotation(
            frontWheelCenter.Value.X,
            frontWheelCenter.Value.Y,
            frontRadiusPx,
            rearWheelCenter.Value.X,
            rearWheelCenter.Value.Y,
            rearRadiusPx);

        return rotationAngle;
    }

    public Rect? GetWheelBounds()
    {
        if (!HasWheels)
        {
            return null;
        }

        var minX = Math.Min(FrontWheelCircleLeft, RearWheelCircleLeft);
        var minY = Math.Min(FrontWheelCircleTop, RearWheelCircleTop);
        var maxX = Math.Max(FrontWheelCircleLeft + FrontWheelCircleDiameter, RearWheelCircleLeft + RearWheelCircleDiameter);
        var maxY = Math.Max(FrontWheelCircleTop + FrontWheelCircleDiameter, RearWheelCircleTop + RearWheelCircleDiameter);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    public bool HasChangesComparedTo(BikeSnapshot snapshot) =>
        FrontWheelRimSize != snapshot.FrontWheelRimSize ||
        !MathUtils.AreEqual(FrontWheelTireWidth, snapshot.FrontWheelTireWidth) ||
        !MathUtils.AreEqual(FrontWheelDiameter, snapshot.FrontWheelDiameterMm) ||
        RearWheelRimSize != snapshot.RearWheelRimSize ||
        !MathUtils.AreEqual(RearWheelTireWidth, snapshot.RearWheelTireWidth) ||
        !MathUtils.AreEqual(RearWheelDiameter, snapshot.RearWheelDiameterMm);

    private bool CanRemoveFrontWheel() =>
        FrontWheelRimSize.HasValue ||
        FrontWheelTireWidth.HasValue ||
        FrontWheelDiameter.HasValue;

    private bool CanRemoveRearWheel() =>
        RearWheelRimSize.HasValue ||
        RearWheelTireWidth.HasValue ||
        RearWheelDiameter.HasValue;

    [RelayCommand(CanExecute = nameof(CanRemoveFrontWheel))]
    private void RemoveFrontWheel()
    {
        WithWheelStateCallbacksSuspended(() =>
        {
            FrontWheelRimSize = null;
            FrontWheelTireWidth = null;
            FrontWheelDiameter = null;
        });

        NotifyFrontWheelPropertiesChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRemoveRearWheel))]
    private void RemoveRearWheel()
    {
        WithWheelStateCallbacksSuspended(() =>
        {
            RearWheelRimSize = null;
            RearWheelTireWidth = null;
            RearWheelDiameter = null;
        });

        NotifyRearWheelPropertiesChanged();
    }

    public void ClearAll()
    {
        WithWheelStateCallbacksSuspended(() =>
        {
            FrontWheelRimSize = null;
            FrontWheelTireWidth = null;
            FrontWheelDiameter = null;
            RearWheelRimSize = null;
            RearWheelTireWidth = null;
            RearWheelDiameter = null;
        });

        NotifyFrontWheelPropertiesChanged();
        NotifyRearWheelPropertiesChanged();
    }

    private void WithWheelStateCallbacksSuspended(Action action)
    {
        suppressWheelStateCallbacks = true;
        try
        {
            action();
        }
        finally
        {
            suppressWheelStateCallbacks = false;
        }
    }

    private double ComputeRimCircleDiameter(EtrtoRimSize? rimSize, double? totalDiameter)
    {
        if (!totalDiameter.HasValue || !pixelsToMillimeters.HasValue) return 0;
        var rimDiameterMm = rimSize?.BeadDiameterMm ?? totalDiameter.Value * 0.83;
        return rimDiameterMm / pixelsToMillimeters.Value;
    }

    private static string FormatWheelDisplay(EtrtoRimSize? rimSize, double? tireWidth, double? diameter)
    {
        if (!diameter.HasValue) return "";

        if (rimSize.HasValue && tireWidth.HasValue)
        {
            return $"{rimSize.Value.DisplayName} / {tireWidth:0.00}\"";
        }

        return $"{diameter:0.0} mm";
    }

    private void RecalculateFrontWheelDiameter()
    {
        if (FrontWheelRimSize.HasValue && FrontWheelTireWidth.HasValue)
        {
            WithWheelStateCallbacksSuspended(() =>
            {
                FrontWheelDiameter = Math.Round(
                    FrontWheelRimSize.Value.CalculateTotalDiameterMm(FrontWheelTireWidth.Value),
                    1);
            });
        }

        NotifyFrontWheelPropertiesChanged();
    }

    private void RecalculateRearWheelDiameter()
    {
        if (RearWheelRimSize.HasValue && RearWheelTireWidth.HasValue)
        {
            WithWheelStateCallbacksSuspended(() =>
            {
                RearWheelDiameter = Math.Round(
                    RearWheelRimSize.Value.CalculateTotalDiameterMm(RearWheelTireWidth.Value),
                    1);
            });
        }

        NotifyRearWheelPropertiesChanged();
    }

    private void NotifyFrontWheelPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasWheels));
        OnPropertyChanged(nameof(FrontWheelRadiusPixels));
        OnPropertyChanged(nameof(FrontWheelCircleLeft));
        OnPropertyChanged(nameof(FrontWheelCircleTop));
        OnPropertyChanged(nameof(FrontWheelCircleDiameter));
        OnPropertyChanged(nameof(FrontRimCircleDiameter));
        OnPropertyChanged(nameof(FrontTireThickness));
        OnPropertyChanged(nameof(FrontHubDiameter));
        OnPropertyChanged(nameof(FrontWheelDisplayText));
    }

    private void NotifyRearWheelPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasWheels));
        OnPropertyChanged(nameof(RearWheelRadiusPixels));
        OnPropertyChanged(nameof(RearWheelCircleLeft));
        OnPropertyChanged(nameof(RearWheelCircleTop));
        OnPropertyChanged(nameof(RearWheelCircleDiameter));
        OnPropertyChanged(nameof(RearRimCircleDiameter));
        OnPropertyChanged(nameof(RearTireThickness));
        OnPropertyChanged(nameof(RearHubDiameter));
        OnPropertyChanged(nameof(RearWheelDisplayText));
    }
}