using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.Kinematics;
using BikeModel = Sufni.App.Bikes.Models.Bike;
using BikeImageCanvasViewModel = Sufni.App.Bikes.ViewModels.Editors.BikeEditorParts.BikeImageCanvasViewModel;
using BikeWheelGeometryViewModel = Sufni.App.Bikes.ViewModels.Editors.BikeEditorParts.BikeWheelGeometryViewModel;
using LeverageRatioBikeEditorViewModel = Sufni.App.Bikes.ViewModels.Editors.Bike.LeverageRatioEditorViewModel;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.LinkageEditing;
using Sufni.App.Bikes.ViewModels.LinkageParts;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;
using Sufni.App.Shared.Common;
using Sufni.App.Shell.Coordinators;
namespace Sufni.App.Bikes.ViewModels.Editors;

public enum BikeRearSuspensionMode
{
    None,
    Linkage,
    LeverageRatio,
}

public sealed record RimSizeOption(EtrtoRimSize Value, string DisplayName);

/// <summary>
/// Editor view model for a bike. Created by <c>IBikeCoordinator</c>
/// from a <see cref="BikeSnapshot"/>; the snapshot's <c>Updated</c>
/// value is kept as <see cref="BaselineUpdated"/> for optimistic
/// conflict detection at save time.
/// </summary>
public partial class BikeEditorViewModel : TabPageViewModelBase
{
    public Guid Id { get; private set; }
    public long BaselineUpdated { get; private set; }
    public bool IsInDatabase { get; private set; }

    #region Private fields

    private readonly IBikeCoordinator bikeCoordinator;
    private readonly IBikeDependencyQuery dependencyQuery;
    // Immutable editor baseline used for dirty checks and reset/conflict reload.
    private BikeSnapshot acceptedSnapshot;
    private readonly CancellableOperation analysisOperation = new();
    private readonly CancellableOperation imageOperation = new();
    private readonly CancellableOperation importOperation = new();
    private readonly CancellableOperation leverageRatioImportOperation = new();
    // Guard edit-time callbacks while a snapshot is being applied.
    private bool isReplacingState;
    private bool suppressRearSuspensionModeChange;
    private string? rearSuspensionLoadError;

    #endregion Private fields

    #region Bike geometry properties

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial double? HeadAngle { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial double? ForksStroke { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial double? ShockStroke { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial double? Chainstay { get; set; }

    [ObservableProperty] public partial double? PixelsToMillimeters { get; set; }

    #endregion Bike geometry properties

    #region Image properties

    public BikeImageCanvasViewModel ImageCanvas { get; } = new();

    #endregion Image properties

    #region Wheel properties

    public BikeWheelGeometryViewModel WheelGeometry { get; } = new();

    #endregion Wheel properties

    #region Linkage editor properties

    public LinkageEditorViewModel LinkageEditor { get; } = new();
    public LeverageRatioBikeEditorViewModel LeverageRatioEditor { get; }

    [ObservableProperty] public partial bool CanChangeRearSuspensionMode { get; set; }
    [ObservableProperty] public partial BikeRearSuspensionMode RearSuspensionMode { get; set; }

    public bool HasRearSuspension => RearSuspensionMode != BikeRearSuspensionMode.None;
    public bool IsHardtailMode => RearSuspensionMode == BikeRearSuspensionMode.None;
    public bool IsLinkageMode => RearSuspensionMode == BikeRearSuspensionMode.Linkage;
    public bool IsLeverageRatioMode => RearSuspensionMode == BikeRearSuspensionMode.LeverageRatio;
    public string RearSuspensionModeDisplayName => RearSuspensionMode switch
    {
        BikeRearSuspensionMode.None => "Hardtail",
        BikeRearSuspensionMode.Linkage => "Linkage",
        BikeRearSuspensionMode.LeverageRatio => "Leverage ratio",
        _ => throw new ArgumentOutOfRangeException()
    };

    #endregion Linkage editor properties

    #region Analysis properties

    [ObservableProperty] public partial CoordinateList? LeverageRatioData { get; set; }
    [ObservableProperty] public partial bool IsPlotBusy { get; set; }

    #endregion Analysis properties

    #region Property change handlers

    partial void OnChainstayChanged(double? value)
    {
        if (IsReplacingState) return;

        if (value is null)
        {
            PixelsToMillimeters = null;
        }
        else
        {
            UpdatePixelsToMillimeters();
        }

        TryClearRearSuspensionLoadError();
    }

    partial void OnPixelsToMillimetersChanged(double? value)
    {
        if (IsReplacingState) return;

        LinkageEditor.SetPixelsToMillimeters(PixelsToMillimeters);
        WheelGeometry.RefreshDerived(GetFrontWheelCenter(), GetRearWheelCenter(), PixelsToMillimeters);
        RecalculateHeadAngle();
        ImageCanvas.RefreshRearAxlePath(PixelsToMillimeters);
        TryClearRearSuspensionLoadError();
    }

    partial void OnRearSuspensionModeChanged(BikeRearSuspensionMode oldValue, BikeRearSuspensionMode newValue)
    {
        OnPropertyChanged(nameof(HasRearSuspension));
        OnPropertyChanged(nameof(IsHardtailMode));
        OnPropertyChanged(nameof(IsLinkageMode));
        OnPropertyChanged(nameof(IsLeverageRatioMode));
        OnPropertyChanged(nameof(RearSuspensionModeDisplayName));
        NotifyEditorCommandStatesChanged();

        if (suppressRearSuspensionModeChange || IsReplacingState)
        {
            return;
        }

        _ = HandleRearSuspensionModeChangedAsync(oldValue, newValue);
    }

    partial void OnCanChangeRearSuspensionModeChanged(bool value)
    {
        LeverageRatioEditor.CanEdit = value;
    }

    #endregion Property change handlers

    #region Constructors

    public BikeEditorViewModel(
        BikeSnapshot snapshot,
        bool isNew,
        IBikeCoordinator bikeCoordinator,
        IBikeDependencyQuery dependencyQuery,
        IShellCoordinator shell,
        IDialogService dialogService,
        IUiThreadDispatcher uiThreadDispatcher)
        : base(shell, dialogService, uiThreadDispatcher)
    {
        this.bikeCoordinator = bikeCoordinator;
        this.dependencyQuery = dependencyQuery;
        IsInDatabase = !isNew;
        acceptedSnapshot = snapshot;
        LeverageRatioEditor = new LeverageRatioBikeEditorViewModel(canEdit: CanChangeRearSuspensionMode);
        LeverageRatioEditor.ImportCommand = ImportLeverageRatioCommand;
        LinkageEditor.PreviewChanged += OnLinkageEditorPreviewChanged;
        LinkageEditor.StateChanged += OnLinkageEditorStateChanged;
        LeverageRatioEditor.Changed += OnLeverageRatioEditorChanged;
        WheelGeometry.PropertyChanged += OnWheelGeometryPropertyChanged;
        ImageCanvas.PropertyChanged += OnImageCanvasPropertyChanged;

        ReplaceState(snapshot, refreshAnalysis: !isNew);

        if (isNew && IsLinkageMode)
        {
            EnsureLinkageSeeded();
        }
    }

    #endregion Constructors

    #region Private methods

    private bool IsReplacingState => isReplacingState;

    private static BikeRearSuspensionMode ModeFromRearSuspension(RearSuspensionSpec rearSuspension) => rearSuspension switch
    {
        RearSuspensionSpec.Hardtail => BikeRearSuspensionMode.None,
        RearSuspensionSpec.Linkage => BikeRearSuspensionMode.Linkage,
        RearSuspensionSpec.LeverageRatio => BikeRearSuspensionMode.LeverageRatio,
        RearSuspensionSpec.LinkageDraft => BikeRearSuspensionMode.Linkage,
        RearSuspensionSpec.LeverageRatioDraft => BikeRearSuspensionMode.LeverageRatio,
        _ => throw new ArgumentOutOfRangeException(nameof(rearSuspension)),
    };

    // Route every state swap through the same raw-apply -> derive -> accept sequence.
    private void ReplaceState(
        BikeSnapshot snapshot,
        bool acceptBaseline = true,
        bool refreshAnalysis = false,
        bool showPlotBusyOverlay = false)
    {
        RunStateReplacement(() => ApplyRawBikeState(snapshot));

        if (acceptBaseline)
        {
            AcceptBaseline(snapshot);
        }

        EvaluateDirtiness();

        if (refreshAnalysis)
        {
            QueuePlotRefresh(showPlotBusyOverlay);
        }
    }

    // Accept a snapshot as both the reset target and the dirtiness baseline.
    private void AcceptBaseline(BikeSnapshot snapshot)
    {
        acceptedSnapshot = snapshot;
        Id = snapshot.Id;
        BaselineUpdated = snapshot.Updated;
    }

    // Suppress edit-time callbacks while raw state is loaded, then rebuild derived UI state once.
    private void RunStateReplacement(Action action)
    {
        Debug.Assert(!isReplacingState, "Nested state replacement is unexpected.");
        if (isReplacingState)
        {
            action();
            return;
        }

        isReplacingState = true;
        try
        {
            action();
        }
        finally
        {
            isReplacingState = false;
        }

        RefreshDerivedEditorState();
    }

    // Copy persisted inputs verbatim; derived display values are rebuilt afterwards.
    private void ApplyRawBikeState(BikeSnapshot snapshot)
    {
        Id = snapshot.Id;
        Name = snapshot.Name;
        HeadAngle = snapshot.HeadAngle;
        ForksStroke = snapshot.ForkStroke;
        ShockStroke = snapshot.ShockStroke;

        SetRearSuspensionModeSilently(ModeFromRearSuspension(snapshot.RearSuspension));
        ApplyRearSuspension(snapshot.RearSuspension, snapshot);
        WheelGeometry.ApplySnapshot(snapshot);
    }

    private void ApplyRearSuspension(RearSuspensionSpec rearSuspension, BikeSnapshot snapshot)
    {
        switch (rearSuspension)
        {
            case RearSuspensionSpec.Linkage linkage:
                Chainstay = snapshot.Chainstay;
                PixelsToMillimeters = snapshot.PixelsToMillimeters;
                ImageCanvas.ApplySnapshot(snapshot.ImageBytes, snapshot.ImageRotationDegrees);
                LinkageEditor.Load(linkage.Spec, ImageCanvas.Image?.Size.Height, snapshot.PixelsToMillimeters);
                LeverageRatioEditor.ReplaceState(null);
                SetRearSuspensionLoadError(null);
                break;

            case RearSuspensionSpec.LeverageRatio leverageRatio:
                Chainstay = null;
                PixelsToMillimeters = null;
                ImageCanvas.ApplySnapshot(null, 0);
                LinkageEditor.Load(null, null, null);
                LeverageRatioEditor.ReplaceState(leverageRatio.Spec);
                SetRearSuspensionLoadError(null);
                break;

            case RearSuspensionSpec.Hardtail:
            case RearSuspensionSpec.LinkageDraft:
            case RearSuspensionSpec.LeverageRatioDraft:
                Chainstay = null;
                PixelsToMillimeters = null;
                ImageCanvas.ApplySnapshot(null, 0);
                LinkageEditor.Load(null, null, null);
                LeverageRatioEditor.ReplaceState(null);
                SetRearSuspensionLoadError(null);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(rearSuspension));
        }
    }

    // Refresh caches and projections that are safe to recompute from the raw editor state.
    private void RefreshDerivedEditorState()
    {
        LinkageEditor.SetPixelsToMillimeters(PixelsToMillimeters);
        WheelGeometry.RefreshDerived(GetFrontWheelCenter(), GetRearWheelCenter(), PixelsToMillimeters);
        ImageCanvas.RefreshLayout(LinkageEditor.GetJointBounds(), WheelGeometry.GetWheelBounds());
        ImageCanvas.RefreshRearAxlePath(PixelsToMillimeters);
    }

    // Materialize the current editor state as a snapshot; the coordinator rebuilds the domain model if needed.
    private BikeSnapshot ToSnapshot(long updated)
    {
        Debug.Assert(HeadAngle is not null);
        Debug.Assert(ForksStroke is not null);

        var rearSuspension = BuildCurrentRearSuspensionSpec();
        var pixelsToMillimeters = RearSuspensionMode == BikeRearSuspensionMode.Linkage
            ? PixelsToMillimeters ?? acceptedSnapshot.PixelsToMillimeters
            : 0;

        return new BikeSnapshot(
            Id,
            Name ?? $"bike {Id}",
            HeadAngle.Value,
            ForksStroke,
            ShockStroke,
            rearSuspension,
            acceptedSnapshot.FrontCompressionDampingCutoffMmPerSecond,
            acceptedSnapshot.FrontReboundDampingCutoffMmPerSecond,
            acceptedSnapshot.RearCompressionDampingCutoffMmPerSecond,
            acceptedSnapshot.RearReboundDampingCutoffMmPerSecond,
            RearSuspensionMode == BikeRearSuspensionMode.Linkage ? Chainstay : null,
            pixelsToMillimeters,
            WheelSpec.FromValues(
                WheelGeometry.FrontWheelDiameter,
                WheelGeometry.FrontWheelRimSize,
                WheelGeometry.FrontWheelTireWidth),
            WheelSpec.FromValues(
                WheelGeometry.RearWheelDiameter,
                WheelGeometry.RearWheelRimSize,
                WheelGeometry.RearWheelTireWidth),
            RearSuspensionMode == BikeRearSuspensionMode.Linkage ? ImageCanvas.ImageRotationDegrees : 0,
            RearSuspensionMode == BikeRearSuspensionMode.Linkage ? ImageCanvas.ImageBytes : [],
            updated);
    }

    private RearSuspensionSpec BuildCurrentRearSuspensionSpec() => RearSuspensionMode switch
    {
        BikeRearSuspensionMode.None => new RearSuspensionSpec.Hardtail(),
        BikeRearSuspensionMode.Linkage when CreateCurrentLinkageSpec() is LinkageSpec linkage => new RearSuspensionSpec.Linkage(linkage),
        BikeRearSuspensionMode.Linkage => new RearSuspensionSpec.LinkageDraft(),
        BikeRearSuspensionMode.LeverageRatio when LeverageRatioEditor.BuildCurrent() is LeverageRatioSpec leverageRatio => new RearSuspensionSpec.LeverageRatio(leverageRatio),
        BikeRearSuspensionMode.LeverageRatio => new RearSuspensionSpec.LeverageRatioDraft(),
        _ => throw new ArgumentOutOfRangeException(nameof(RearSuspensionMode)),
    };

    private RearSuspension? BuildCurrentRearSuspension() => RearSuspensionMode switch
    {
        BikeRearSuspensionMode.None => null,
        BikeRearSuspensionMode.Linkage when CreateCurrentLinkageSpec() is LinkageSpec linkage => new LinkageRearSuspension(Linkage.FromSpec(linkage)),
        BikeRearSuspensionMode.LeverageRatio when LeverageRatioEditor.BuildCurrent() is LeverageRatioSpec leverageRatio => new LeverageRatioRearSuspension(leverageRatio),
        _ => null,
    };

    private LinkageSpec? CreateCurrentLinkageSpec()
    {
        if (!IsLinkageMode)
        {
            return null;
        }

        return LinkageEditor.BuildCurrentLinkageSpec(ImageCanvas.Image?.Size.Height, PixelsToMillimeters, ShockStroke);
    }

    private JointViewModel? GetFrontWheelJoint() =>
        LinkageEditor.GetFrontWheelJoint();

    private JointViewModel? GetRearWheelJoint() =>
        LinkageEditor.GetRearWheelJoint();

    private Point? GetFrontWheelCenter() => LinkageEditor.GetFrontWheelCenter();

    private Point? GetRearWheelCenter() => LinkageEditor.GetRearWheelCenter();

    private void RecalculateGroundRotation()
    {
        if (ImageCanvas.Image is null) return;

        var deltaRotation = WheelGeometry.TryComputeGroundAlignmentDelta(
            GetFrontWheelCenter(),
            GetRearWheelCenter(),
            PixelsToMillimeters);

        if (!deltaRotation.HasValue || Math.Abs(deltaRotation.Value) <= 0.01) return;

        LinkageEditor.RotateAll(deltaRotation.Value);
        WheelGeometry.RefreshDerived(GetFrontWheelCenter(), GetRearWheelCenter(), PixelsToMillimeters);
        ImageCanvas.ImageRotationDegrees += deltaRotation.Value;
    }

    private void UpdatePixelsToMillimeters()
    {
        var bb = LinkageEditor.JointViewModels.FirstOrDefault(p => p.Type == JointType.BottomBracket);
        var rw = LinkageEditor.JointViewModels.FirstOrDefault(p => p.Type == JointType.RearWheel);
        if (bb is null || rw is null) return;

        // Invalid chainstay input clears the derived scale instead of keeping
        // a stale value, so the editor visibly reflects the degenerate input.
        PixelsToMillimeters = GeometryUtils.CalculatePixelsToMillimetersFromChainstay(Chainstay, rw, bb);
    }

    private void RecalculateHeadAngle()
    {
        var mapping = new JointNameMapping();
        var headTube1 = LinkageEditor.JointViewModels.FirstOrDefault(joint => joint.Name == mapping.HeadTube1);
        var headTube2 = LinkageEditor.JointViewModels.FirstOrDefault(joint => joint.Name == mapping.HeadTube2);
        var frontWheel = GetFrontWheelJoint();
        var rearWheel = GetRearWheelJoint();

        // No head-tube/wheel joints means the angle is not photo-derived here;
        // leave any manually entered value alone.
        if (headTube1 is null || headTube2 is null || frontWheel is null || rearWheel is null) return;

        if (PixelsToMillimeters is null || !WheelGeometry.FrontWheelDiameter.HasValue || !WheelGeometry.RearWheelDiameter.HasValue)
        {
            HeadAngle = null;
            return;
        }

        HeadAngle = GeometryUtils.CalculateHeadAngle(
            headTube1,
            headTube2,
            frontWheel,
            rearWheel,
            WheelGeometry.FrontWheelDiameter.Value,
            WheelGeometry.RearWheelDiameter.Value,
            PixelsToMillimeters.Value);
    }

    private void QueuePlotRefresh(bool showPlotBusyOverlay = true) => _ = RefreshAnalysisAsync(showPlotBusyOverlay);

    private async Task RefreshAnalysisAsync(bool showPlotBusyOverlay = false)
    {
        var token = analysisOperation.Start();
        IsPlotBusy = showPlotBusyOverlay;
        try
        {
            var result = await bikeCoordinator.LoadAnalysisAsync(BuildCurrentRearSuspension(), token);
            if (token.IsCancellationRequested) return;

            ApplyAnalysisResult(result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            if (token.IsCancellationRequested) return;
            ApplyAnalysisResult(new BikeEditorAnalysisResult.Failed(e.Message));
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsPlotBusy = false;
        }
    }

    private void ApplyAnalysisResult(BikeEditorAnalysisResult result)
    {
        switch (result)
        {
            case BikeEditorAnalysisResult.Computed computed:
                LeverageRatioData = computed.Data.LeverageRatioData;
                ImageCanvas.SetRearAxlePathData(computed.Data.RearAxlePathData);
                ImageCanvas.RefreshRearAxlePath(PixelsToMillimeters);
                break;
            case BikeEditorAnalysisResult.Unavailable:
                LeverageRatioData = null;
                ImageCanvas.SetRearAxlePathData(null);
                ImageCanvas.RefreshRearAxlePath(PixelsToMillimeters);
                break;
            case BikeEditorAnalysisResult.Failed failed:
                LeverageRatioData = null;
                ImageCanvas.SetRearAxlePathData(null);
                ImageCanvas.RefreshRearAxlePath(PixelsToMillimeters);
                ErrorMessages.Add($"Rear suspension analysis failed: {failed.ErrorMessage}");
                break;
        }
    }

    private void ApplyImportedBike(ImportedBikeEditorData data)
    {
        var importedSnapshot = BikeSnapshot.From(data.Bike);
        IsInDatabase = false;

        ReplaceState(importedSnapshot);
        ApplyAnalysisResult(data.AnalysisResult);

        NotifyDeleteCommandStateChanged();
    }

    private void NotifyEditorCommandStatesChanged()
    {
        SaveCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        ImportLeverageRatioCommand.NotifyCanExecuteChanged();
    }

    private void RefreshLinkagePreview()
    {
        UpdatePixelsToMillimeters();
        WheelGeometry.RefreshDerived(GetFrontWheelCenter(), GetRearWheelCenter(), PixelsToMillimeters);
        ImageCanvas.RefreshLayout(LinkageEditor.GetJointBounds(), WheelGeometry.GetWheelBounds());
        RecalculateHeadAngle();
        TryClearRearSuspensionLoadError();
    }

    private void RefreshLinkageState()
    {
        RefreshLinkagePreview();
        LinkageEditor.SetPixelsToMillimeters(PixelsToMillimeters);
    }

    private void HandleLinkagePreviewChanged(LinkagePreviewChangedEventArgs args)
    {
        if (IsReplacingState || !IsLinkageMode)
        {
            return;
        }

        if (args.Joint is null)
        {
            RefreshLinkagePreview();
            return;
        }

        switch (args.Joint.Type)
        {
            case JointType.BottomBracket:
                UpdatePixelsToMillimeters();
                break;

            case JointType.FrontWheel:
                WheelGeometry.RefreshDerived(GetFrontWheelCenter(), GetRearWheelCenter(), PixelsToMillimeters);
                break;

            case JointType.RearWheel:
                UpdatePixelsToMillimeters();
                WheelGeometry.RefreshDerived(GetFrontWheelCenter(), GetRearWheelCenter(), PixelsToMillimeters);
                break;
        }

        ImageCanvas.RefreshLayout(LinkageEditor.GetJointBounds(), WheelGeometry.GetWheelBounds());
        RecalculateHeadAngle();
        TryClearRearSuspensionLoadError();
    }

    internal void HandleLinkagePreviewChangedForTests(LinkagePreviewChangedEventArgs args)
    {
        HandleLinkagePreviewChanged(args);
    }

    private void OnLinkageEditorPreviewChanged(object? sender, LinkagePreviewChangedEventArgs e)
    {
        HandleLinkagePreviewChanged(e);
    }

    private void OnLinkageEditorStateChanged(object? sender, EventArgs e)
    {
        if (IsReplacingState || !IsLinkageMode)
        {
            return;
        }

        RefreshLinkageState();
        EvaluateDirtiness();
        NotifyEditorCommandStatesChanged();
    }

    private void OnWheelGeometryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName))
        {
            return;
        }

        if (AffectsWheelCanvasBounds(e.PropertyName))
        {
            ImageCanvas.RefreshLayout(LinkageEditor.GetJointBounds(), WheelGeometry.GetWheelBounds());
        }

        if (IsReplacingState || !IsWheelInputProperty(e.PropertyName))
        {
            return;
        }

        WheelGeometry.RefreshDerived(GetFrontWheelCenter(), GetRearWheelCenter(), PixelsToMillimeters);

        if (e.PropertyName is nameof(BikeWheelGeometryViewModel.FrontWheelDiameter) or nameof(BikeWheelGeometryViewModel.RearWheelDiameter))
        {
            RecalculateGroundRotation();
        }

        RecalculateHeadAngle();
        NotifyEditorCommandStatesChanged();
        EvaluateDirtiness();
    }

    private void OnImageCanvasPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName))
        {
            return;
        }

        if (IsReplacingState)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(BikeImageCanvasViewModel.Image):
                RefreshDerivedEditorState();
                NotifyEditorCommandStatesChanged();
                EvaluateDirtiness();
                TryClearRearSuspensionLoadError();
                break;

            case nameof(BikeImageCanvasViewModel.ImageRotationDegrees):
                ImageCanvas.RefreshLayout(LinkageEditor.GetJointBounds(), WheelGeometry.GetWheelBounds());
                NotifyEditorCommandStatesChanged();
                EvaluateDirtiness();
                break;
        }
    }

    private void OnLeverageRatioEditorChanged(object? sender, EventArgs e)
    {
        if (IsReplacingState)
        {
            return;
        }

        TryClearRearSuspensionLoadError();
        EvaluateDirtiness();
        NotifyEditorCommandStatesChanged();
        QueuePlotRefresh(showPlotBusyOverlay: false);
    }

    private async Task HandleRearSuspensionModeChangedAsync(
        BikeRearSuspensionMode oldValue,
        BikeRearSuspensionMode newValue)
    {
        if (oldValue == newValue)
        {
            return;
        }

        if (HasOutgoingPayload(oldValue))
        {
            var confirmed = await dialogService.ShowConfirmationAsync(
                "Discard rear suspension data?",
                $"Switching to {ModeLabel(newValue)} will discard the current {ModeLabel(oldValue)} data. Continue?");
            if (!confirmed)
            {
                SetRearSuspensionModeSilently(oldValue);
                return;
            }
        }

        ResetOutgoingPayload(oldValue);
        SetRearSuspensionLoadError(null);

        if (newValue == BikeRearSuspensionMode.None)
        {
            ShockStroke = null;
        }

        if (newValue == BikeRearSuspensionMode.Linkage)
        {
            EnsureLinkageSeeded();
        }

        EvaluateDirtiness();
        NotifyEditorCommandStatesChanged();
        QueuePlotRefresh(showPlotBusyOverlay: false);
    }

    private void SetRearSuspensionModeSilently(BikeRearSuspensionMode mode)
    {
        suppressRearSuspensionModeChange = true;
        try
        {
            RearSuspensionMode = mode;
        }
        finally
        {
            suppressRearSuspensionModeChange = false;
        }
    }

    private bool HasOutgoingPayload(BikeRearSuspensionMode mode)
    {
        return mode switch
        {
            BikeRearSuspensionMode.None => false,
            BikeRearSuspensionMode.Linkage =>
                CreateCurrentLinkageSpec() is not null ||
                ImageCanvas.Image is not null ||
                Chainstay is not null,
            BikeRearSuspensionMode.LeverageRatio =>
                LeverageRatioEditor.BuildCurrent() is not null,
            _ => false,
        };
    }

    private void ResetOutgoingPayload(BikeRearSuspensionMode mode)
    {
        switch (mode)
        {
            case BikeRearSuspensionMode.None:
                return;

            case BikeRearSuspensionMode.Linkage:
                Chainstay = null;
                PixelsToMillimeters = null;
                WheelGeometry.ClearAll();
                ImageCanvas.ApplySnapshot(null, 0);
                ImageCanvas.SetRearAxlePathData(null);
                LinkageEditor.Load(null, null, null);
                break;

            case BikeRearSuspensionMode.LeverageRatio:
                LeverageRatioEditor.ReplaceState(null);
                break;
        }
    }

    private void EnsureLinkageSeeded()
    {
        if (LinkageEditor.JointViewModels.Count > 0 || LinkageEditor.LinkViewModels.Count > 0)
        {
            return;
        }

        LinkageEditor.AddInitialJoints();
    }

    private void SetRearSuspensionLoadError(string? errorMessage)
    {
        if (rearSuspensionLoadError is not null)
        {
            ErrorMessages.Remove(rearSuspensionLoadError);
        }

        rearSuspensionLoadError = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage;
        if (rearSuspensionLoadError is not null)
        {
            ErrorMessages.Add(rearSuspensionLoadError);
        }
    }

    private void TryClearRearSuspensionLoadError()
    {
        if (rearSuspensionLoadError is null)
        {
            return;
        }

        if (IsRearSuspensionStateResolved())
        {
            SetRearSuspensionLoadError(null);
        }
    }

    private bool IsRearSuspensionStateResolved() => RearSuspensionMode switch
    {
        BikeRearSuspensionMode.None => true,
        BikeRearSuspensionMode.Linkage => CreateCurrentLinkageSpec() is not null,
        BikeRearSuspensionMode.LeverageRatio => LeverageRatioEditor.BuildCurrent() is not null,
        _ => false,
    };

    private static string ModeLabel(BikeRearSuspensionMode mode) => mode switch
    {
        BikeRearSuspensionMode.None => "hardtail",
        BikeRearSuspensionMode.Linkage => "linkage",
        BikeRearSuspensionMode.LeverageRatio => "leverage ratio",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static bool LeverageRatiosEqual(LeverageRatioSpec? left, LeverageRatioSpec? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Points.SequenceEqual(right.Points);
    }

    private static bool IsWheelInputProperty(string propertyName) =>
        propertyName is nameof(BikeWheelGeometryViewModel.FrontWheelRimSize) or
            nameof(BikeWheelGeometryViewModel.FrontWheelTireWidth) or
            nameof(BikeWheelGeometryViewModel.FrontWheelDiameter) or
            nameof(BikeWheelGeometryViewModel.RearWheelRimSize) or
            nameof(BikeWheelGeometryViewModel.RearWheelTireWidth) or
            nameof(BikeWheelGeometryViewModel.RearWheelDiameter);

    private static bool AffectsWheelCanvasBounds(string propertyName) =>
        propertyName is nameof(BikeWheelGeometryViewModel.HasWheels) or
            nameof(BikeWheelGeometryViewModel.FrontWheelCircleLeft) or
            nameof(BikeWheelGeometryViewModel.FrontWheelCircleTop) or
            nameof(BikeWheelGeometryViewModel.FrontWheelCircleDiameter) or
            nameof(BikeWheelGeometryViewModel.RearWheelCircleLeft) or
            nameof(BikeWheelGeometryViewModel.RearWheelCircleTop) or
            nameof(BikeWheelGeometryViewModel.RearWheelCircleDiameter);

    #endregion Private methods

    #region TabPageViewModelBase overrides

    protected override void EvaluateDirtiness()
    {
        var acceptedMode = ModeFromRearSuspension(acceptedSnapshot.RearSuspension);
        var acceptedLinkage = (acceptedSnapshot.RearSuspension as RearSuspensionSpec.Linkage)?.Spec;
        var acceptedLeverageRatio = (acceptedSnapshot.RearSuspension as RearSuspensionSpec.LeverageRatio)?.Spec;
        var linkageDirty = IsLinkageMode &&
            (!MathUtils.AreEqual(Chainstay, acceptedMode == BikeRearSuspensionMode.Linkage ? acceptedSnapshot.Chainstay : null) ||
             LinkageEditor.HasChangesComparedTo(
                 acceptedMode == BikeRearSuspensionMode.Linkage ? acceptedLinkage : null,
                 ImageCanvas.Image?.Size.Height,
                 PixelsToMillimeters) ||
             (acceptedMode == BikeRearSuspensionMode.Linkage
                 ? ImageCanvas.HasChangesComparedTo(acceptedSnapshot)
                 : ImageCanvas.Image is not null || !MathUtils.AreEqual(ImageCanvas.ImageRotationDegrees, 0)));
        var leverageRatioDirty = IsLeverageRatioMode &&
            !LeverageRatiosEqual(
                LeverageRatioEditor.BuildCurrent(),
                acceptedMode == BikeRearSuspensionMode.LeverageRatio ? acceptedLeverageRatio : null);

        IsDirty =
            !IsInDatabase ||
            Name != acceptedSnapshot.Name ||
            !MathUtils.AreEqual(HeadAngle, acceptedSnapshot.HeadAngle) ||
            !MathUtils.AreEqual(ForksStroke, acceptedSnapshot.ForkStroke) ||
            !MathUtils.AreEqual(ShockStroke, acceptedSnapshot.ShockStroke) ||
            RearSuspensionMode != acceptedMode ||
            linkageDirty ||
            leverageRatioDirty ||
            WheelGeometry.HasChangesComparedTo(acceptedSnapshot);
    }

    protected override bool CanSave()
    {
        EvaluateDirtiness();

        var frontHasDiameter = WheelGeometry.FrontWheelDiameter.HasValue;
        var rearHasDiameter = WheelGeometry.RearWheelDiameter.HasValue;
        var wheelsValid = RearSuspensionMode == BikeRearSuspensionMode.LeverageRatio || frontHasDiameter == rearHasDiameter;
        var rearSuspensionValid = RearSuspensionMode switch
        {
            BikeRearSuspensionMode.None => true,
            BikeRearSuspensionMode.Linkage =>
                ShockStroke is not null &&
                ImageCanvas.Image is not null &&
                Chainstay is not null &&
                CreateCurrentLinkageSpec() is not null,
            BikeRearSuspensionMode.LeverageRatio =>
                ShockStroke is not null &&
                LeverageRatioEditor.BuildCurrent() is not null,
            _ => false,
        };

        return IsDirty &&
               rearSuspensionLoadError is null &&
               HeadAngle is not null &&
               ForksStroke is not null &&
               rearSuspensionValid &&
               wheelsValid;
    }

    protected override async Task SaveImplementation()
    {
        if (IsLinkageMode)
        {
            RecalculateGroundRotation();
        }

        var snapshot = ToSnapshot(BaselineUpdated);
        var bike = BikeModel.FromSnapshot(snapshot);
        var result = await bikeCoordinator.SaveAsync(bike, BaselineUpdated);

        switch (result)
        {
            case BikeSaveResult.Saved saved:
                bike.Updated = saved.NewBaselineUpdated;
                AcceptBaseline(BikeSnapshot.From(bike));
                IsInDatabase = true;

                ApplyAnalysisResult(saved.AnalysisResult);
                EvaluateDirtiness();

                NotifyDeleteCommandStateChanged();
                break;

            case BikeSaveResult.Conflict conflict:
                var reload = await dialogService.ShowConfirmationAsync(
                    "Bike changed elsewhere",
                    "This bike has been updated from another source. Discard your changes and reload?");
                if (reload)
                {
                    IsInDatabase = true;
                    ReplaceState(conflict.CurrentSnapshot, refreshAnalysis: true, showPlotBusyOverlay: true);
                    NotifyDeleteCommandStateChanged();
                }
                break;

            case BikeSaveResult.InvalidLinkage:
                ErrorMessages.Add("Linkage movement could not be calculated. Please check the joints and links!");
                break;

            case BikeSaveResult.InvalidRearSuspension invalidRearSuspension:
                ErrorMessages.Add(invalidRearSuspension.ErrorMessage);
                break;

            case BikeSaveResult.Failed failed:
                ErrorMessages.Add($"Bike could not be saved: {failed.ErrorMessage}");
                break;
        }
    }

    protected override bool CanDelete() =>
        !IsInDatabase || !dependencyQuery.IsBikeInUse(Id);

    protected override async Task DeleteImplementation(bool navigateBack)
    {
        if (!IsInDatabase)
        {
            if (navigateBack) OpenPreviousPage();
            return;
        }

        var result = await bikeCoordinator.DeleteAsync(Id);
        switch (result.Outcome)
        {
            case BikeDeleteOutcome.Deleted:
                if (navigateBack) OpenPreviousPage();
                break;
            case BikeDeleteOutcome.InUse:
                ErrorMessages.Add("Bike is referenced by a setup and cannot be deleted.");
                break;
            case BikeDeleteOutcome.Failed:
                ErrorMessages.Add($"Bike could not be deleted: {result.ErrorMessage}");
                break;
        }
    }

    protected override Task ResetImplementation()
    {
        ReplaceState(acceptedSnapshot, acceptBaseline: false);
        return RefreshAnalysisAsync(showPlotBusyOverlay: true);
    }

    protected override async Task ExportImplementation()
    {
        var result = await bikeCoordinator.ExportBikeAsync(BikeModel.FromSnapshot(ToSnapshot(BaselineUpdated)));
        if (result is BikeExportResult.Failed failed)
        {
            ErrorMessages.Add($"Bike could not be exported: {failed.ErrorMessage}");
        }
    }

    #endregion TabPageViewModelBase overrides

    #region Commands

    [RelayCommand]
    private void SetRearSuspensionMode(BikeRearSuspensionMode mode)
    {
        if (!CanChangeRearSuspensionMode || RearSuspensionMode == mode)
        {
            return;
        }

        RearSuspensionMode = mode;
    }

    [RelayCommand]
    private async Task OpenImage()
    {
        var token = imageOperation.Start();
        try
        {
            var result = await bikeCoordinator.LoadImageAsync(token);
            if (token.IsCancellationRequested) return;

            switch (result)
            {
                case BikeImageLoadResult.Loaded loaded:
                    ImageCanvas.ApplyLoadedImage(loaded.ImageBytes, loaded.Bitmap);
                    break;
                case BikeImageLoadResult.Failed failed:
                    ErrorMessages.Add($"Bike image could not be loaded: {failed.ErrorMessage}");
                    break;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private bool CanImportLeverageRatio() =>
        LeverageRatioEditor.CanEdit &&
        IsLeverageRatioMode;

    [RelayCommand(CanExecute = nameof(CanImportLeverageRatio))]
    private async Task ImportLeverageRatio()
    {
        var token = leverageRatioImportOperation.Start();
        try
        {
            var result = await bikeCoordinator.ImportLeverageRatioAsync(token);
            if (token.IsCancellationRequested) return;

            LeverageRatioEditor.ApplyImportResult(result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    [RelayCommand]
    private async Task Import()
    {
        if (IsDirty)
        {
            var discard = await dialogService.ShowConfirmationAsync(
                "Discard unsaved changes?",
                "Importing will replace your unsaved changes. Continue?");
            if (!discard) return;
        }

        analysisOperation.Cancel();
        var token = importOperation.Start();
        try
        {
            var result = await bikeCoordinator.ImportBikeAsync(token);
            if (token.IsCancellationRequested) return;

            switch (result)
            {
                case BikeImportResult.Imported imported:
                    ApplyImportedBike(imported.Data);
                    break;
                case BikeImportResult.InvalidFile invalid:
                    ErrorMessages.Add(invalid.ErrorMessage);
                    break;
                case BikeImportResult.Failed failed:
                    ErrorMessages.Add($"Bike could not be imported: {failed.ErrorMessage}");
                    break;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    [RelayCommand]
    private async Task Loaded()
    {
        // Refresh Delete CanExecute when "in use" status changes.
        EnsureScopedSubscription(s => s.Add(dependencyQuery.Changes.Subscribe(_ =>
        {
            NotifyDeleteCommandStateChanged();
        })));

        await RefreshAnalysisAsync();
    }

    [RelayCommand]
    private void Unloaded()
    {
        DisposeScopedSubscriptions();
        analysisOperation.Cancel();
        imageOperation.Cancel();
        importOperation.Cancel();
        leverageRatioImportOperation.Cancel();
    }

    #endregion Commands
}
