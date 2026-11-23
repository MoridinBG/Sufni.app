using System;
using System.Collections.Immutable;
using Avalonia;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.LinkageEditing;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.ViewModels.Editors;

public enum BikeEditorPhase { Loading, Ready, Unavailable, Closed }

/// <summary>Why a draft enters the editor; it decides baseline acceptance, failure recovery and analysis timing.</summary>
public enum BikeDraftLoadKind { Open, Reset, ConflictReload, ImageRecovery }

/// <summary>An image and its pixel size. The instance identifies the image: the canvas shows a bitmap only for the instance in the current state.</summary>
public sealed class BikeEditorImage(ReadOnlyMemory<byte> bytes, Size size)
{
    public ReadOnlyMemory<byte> Bytes { get; } = bytes.ToArray();
    public Size Size { get; } = size;
}

public sealed record BikeEditorInputs
{
    public double? ForkStroke { get; init; }
    public double? ShockStroke { get; init; }
    public double? ManualHeadAngle { get; init; }
    public RearSuspensionKind Mode { get; init; }
    public double? Chainstay { get; init; }
    public WheelSpec? FrontWheel { get; init; }
    public WheelSpec? RearWheel { get; init; }
    public LinkageDraft Linkage { get; init; } = LinkageDraft.Empty;
    public BikeEditorImage? Image { get; init; }
    public double Rotation { get; init; }
    public LeverageRatioSpec? Curve { get; init; }
    public ImmutableArray<string> ImportErrors { get; init; } = [];
}

/// <summary>The analysis to run. A completion is accepted only while its generation is current.</summary>
public sealed record BikeAnalysisRequest(long Generation, RearSuspensionSpec? Spec, bool Debounce, bool Busy);
public sealed record BikeAnalysisPresentation(RearSuspensionSpec Spec, BikeAnalysisPresentationData Data);

public sealed record BikeWheelOverlay(double Left, double Top, double Diameter, double RimDiameter)
{
    public double TireThickness => (Diameter - RimDiameter) / 2;
    public double HubDiameter => Diameter * 0.08;
}

public sealed record BikeEditorGeometry(
    double? Scale, double? EffectiveHeadAngle, bool IsHeadAngleDerived, RearSuspensionSpec Spec,
    bool RequiredInputs, bool InputsDirty, BikeWheelOverlay Front, BikeWheelOverlay Rear, bool HasWheels,
    Rect ImageBounds, Rect ContentBounds, ImmutableArray<Point> RearAxlePath, ImmutableDictionary<int, double> LinkLengths);

public sealed record BikeEditorState(BikeDraft Baseline, bool IsPersisted)
{
    public BikeEditorPhase Phase { get; init; } = BikeEditorPhase.Loading;
    public BikeEditorInputs Inputs { get; init; } = new();
    public int? Selection { get; init; }
    public bool OverlayVisible { get; init; }
    // No analysis runs while the view is detached.
    public bool AnalysisSuspended { get; init; }
    public BikeAnalysisRequest Request { get; init; } = new(0, null, false, false);
    public BikeAnalysisPresentation? Presentation { get; init; }
    public BikeEditorGeometry Geometry { get; private init; } = BikeEditorGeometryDeriver.Derive(new(), Baseline, null);

    internal BikeEditorState Derive() => this with { Geometry = BikeEditorGeometryDeriver.Derive(Inputs, Baseline, Presentation) };

    public BikeDraft Capture(string name) => new(Baseline.Id, name, Geometry.EffectiveHeadAngle, Inputs.ForkStroke,
        Inputs.ShockStroke, Geometry.Spec, Inputs.Mode == RearSuspensionKind.Linkage ? Geometry.Scale ?? Baseline.PixelsToMillimeters : 0,
        Inputs.FrontWheel, Inputs.RearWheel, Inputs.Mode == RearSuspensionKind.Linkage ? Inputs.Rotation : 0,
        Inputs.Mode == RearSuspensionKind.Linkage ? Inputs.Image?.Bytes.ToArray() ?? [] : [])
        { InitialDampingSpeedCutoffs = Baseline.InitialDampingSpeedCutoffs };
}
