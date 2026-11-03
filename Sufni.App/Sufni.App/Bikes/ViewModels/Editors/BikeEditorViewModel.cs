using Sufni.App.Shared.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Sufni.App.Bikes.ViewModels.Editors.BikeEditorParts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.Kinematics;
using BikeModel = Sufni.App.Bikes.Models.Bike;
using BikeImageCanvasViewModel = Sufni.App.Bikes.ViewModels.Editors.BikeEditorParts.BikeImageCanvasViewModel;
using BikeWheelGeometryViewModel = Sufni.App.Bikes.ViewModels.Editors.BikeEditorParts.BikeWheelGeometryViewModel;
using LeverageRatioBikeEditorViewModel = Sufni.App.Bikes.ViewModels.Editors.Bike.LeverageRatioEditorViewModel;

using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.LinkageEditing;
using Sufni.App.Bikes.ViewModels.LinkageParts;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;
using Sufni.App.Shared.Common;
using Sufni.App.Shell.Coordinators;
namespace Sufni.App.Bikes.ViewModels.Editors;

public sealed record RimSizeOption(EtrtoRimSize Value, string DisplayName);

/// <summary>
/// Bike editor created by the editor factory from a snapshot. One immutable
/// draft base supports reset, dirty checks and authored-field conflicts.
/// </summary>
public partial class BikeEditorViewModel : TabPageViewModelBase
{
    public Guid Id { get; private set; }
    public bool IsInDatabase { get; private set; }

    #region Private fields

    private readonly IBikeCoordinator bikeCoordinator;
    private readonly IBikeDependencyQuery dependencyQuery;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly CancellableOperation stateOperation = new();
    private Task stateLoadTask = Task.CompletedTask;
    private bool draftReady;
    [ObservableProperty] public partial bool IsLoadingDraft { get; private set; }
    // Immutable editor baseline used for dirty checks and reset/conflict reload.
    private BikeDraft draftBase;
    private readonly CancellableOperation analysisOperation = new();
    private RearSuspensionSpec? analysisSpec;
    private Task analysisTask = Task.CompletedTask;
    private readonly CancellableOperation imageOperation = new();
    private readonly CancellableOperation leverageRatioImportOperation = new();
    // Guard edit-time callbacks while a snapshot is being applied.
    private bool isReplacingState;

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

    public bool CanChangeRearSuspensionMode { get; }
    [ObservableProperty] public partial RearSuspensionKind RearSuspensionMode { get; private set; }

    public bool HasRearSuspension => RearSuspensionMode != RearSuspensionKind.None;
    public bool IsHardtailMode => RearSuspensionMode == RearSuspensionKind.None;
    public bool IsLinkageMode => RearSuspensionMode == RearSuspensionKind.Linkage;
    public bool IsLeverageRatioMode => RearSuspensionMode == RearSuspensionKind.LeverageRatio;
    public string RearSuspensionModeDisplayName => RearSuspensionMode switch
    {
        RearSuspensionKind.None => "Hardtail",
        RearSuspensionKind.Linkage => "Linkage",
        RearSuspensionKind.LeverageRatio => "Leverage ratio",
        _ => throw new ArgumentOutOfRangeException()
    };

    #endregion Linkage editor properties

    #region Analysis properties

    [ObservableProperty] public partial CoordinateList? LeverageRatioData { get; set; }
    [ObservableProperty] public partial bool IsPlotBusy { get; set; }

    #endregion Analysis properties

    #region Property change handlers

    partial void OnShockStrokeChanged(double? value)
    {
        if (!IsReplacingState) QueuePlotRefresh();
    }

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

    }

    partial void OnPixelsToMillimetersChanged(double? value)
    {
        if (IsReplacingState) return;

        LinkageEditor.SetPixelsToMillimeters(PixelsToMillimeters);
        WheelGeometry.RefreshDerived(GetFrontWheelCenter(), GetRearWheelCenter(), PixelsToMillimeters);
        RecalculateHeadAngle();
        ImageCanvas.RefreshRearAxlePath(PixelsToMillimeters);
        QueuePlotRefresh();
    }

    partial void OnRearSuspensionModeChanged(RearSuspensionKind oldValue, RearSuspensionKind newValue)
    {
        OnPropertyChanged(nameof(HasRearSuspension));
        OnPropertyChanged(nameof(IsHardtailMode));
        OnPropertyChanged(nameof(IsLinkageMode));
        OnPropertyChanged(nameof(IsLeverageRatioMode));
        OnPropertyChanged(nameof(RearSuspensionModeDisplayName));
        NotifyEditorCommandStatesChanged();

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
        IUiThreadDispatcher uiThreadDispatcher,
        InputCapabilities inputCapabilities,
        IBackgroundTaskRunner backgroundTaskRunner,
        TimeProvider timeProvider,
        BikeDraft? initialDraft = null)
        : base(shell, dialogService, uiThreadDispatcher, timeProvider: timeProvider)
    {
        this.bikeCoordinator = bikeCoordinator;
        this.backgroundTaskRunner = backgroundTaskRunner;
        Id = snapshot.Id;
        Name = snapshot.Name;
        var needsStoredDraft = snapshot.HasImage && initialDraft is null;
        this.dependencyQuery = dependencyQuery;
        CanChangeRearSuspensionMode = inputCapabilities.HasPointer;
        IsInDatabase = !isNew;
        draftBase = initialDraft ?? BikeDraft.From(snapshot);
        LeverageRatioEditor = new LeverageRatioBikeEditorViewModel(canEdit: CanChangeRearSuspensionMode);
        LeverageRatioEditor.ImportLeverageRatioCommand = ImportLeverageRatioCommand;
        LinkageEditor.PreviewChanged += OnLinkageEditorPreviewChanged;
        LinkageEditor.StateChanged += OnLinkageEditorStateChanged;
        LeverageRatioEditor.Changed += OnLeverageRatioEditorChanged;
        WheelGeometry.PropertyChanged += OnWheelGeometryPropertyChanged;
        ImageCanvas.PropertyChanged += OnImageCanvasPropertyChanged;

        stateLoadTask = ReplaceStateAsync(draftBase, readStoredDraft: needsStoredDraft, refreshAnalysis: !isNew);
    }

    #endregion Constructors

    #region Private methods

    private bool IsReplacingState => isReplacingState;

    private async Task ReplaceStateAsync(
        BikeDraft draft,
        bool readStoredDraft = false,
        bool acceptBaseline = true,
        bool refreshAnalysis = false,
        bool showPlotBusyOverlay = false)
    {
        var token = stateOperation.Start();
        IsLoadingDraft = true;
        try
        {
            if (readStoredDraft)
            {
                var result = await bikeCoordinator.LoadDraftAsync(draft.Id, token);
                if (token.IsCancellationRequested) return;
                switch (result)
                {
                    case BikeDraftLoadResult.Loaded loaded:
                        draft = loaded.Draft;
                        break;
                    case BikeDraftLoadResult.Missing:
                        Notifications.Add("The bike is no longer available.");
                        return;
                    case BikeDraftLoadResult.Failed failed:
                        ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                        return;
                }
            }

            if (!draftReady && acceptBaseline) AcceptBaseline(draft);
            var decoded = draft.RearSuspension.Kind == RearSuspensionKind.Linkage && !draft.ImageBytesMemory.IsEmpty
                ? await backgroundTaskRunner.RunAsync(() =>
                {
                    var bytes = draft.ImageBytes;
                    return (Bytes: bytes, Image: BikeImageData.Decode(bytes));
                }, token)
                : (Bytes: Array.Empty<byte>(), Image: (Bitmap?)null);
            if (token.IsCancellationRequested)
            {
                decoded.Image?.Dispose();
                return;
            }
            RunStateReplacement(() => ApplyRawBikeState(draft, decoded.Bytes, decoded.Image));
            draftReady = true;
            if (acceptBaseline) AcceptBaseline(draft);
            if (!IsInDatabase && IsLinkageMode) EnsureLinkageSeeded();
            EvaluateDirtiness();
            if (refreshAnalysis) _ = RefreshAnalysisAsync(showPlotBusyOverlay);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (token.IsCancellationRequested) return;
            Log.ForContext<BikeEditorViewModel>().Warning(exception, "Bike image could not be decoded for {BikeId}", Id);
            ErrorMessages.Add(FailurePresentation.From(BikeImageFailureReason.Decode));
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsLoadingDraft = false;
                NotifyEditorCommandStatesChanged();
            }
        }
    }

    private void AcceptBaseline(BikeDraft draft)
    {
        draftBase = draft;
        Id = draft.Id;
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
    private void ApplyRawBikeState(BikeDraft snapshot, byte[] imageBytes, Bitmap? image)
    {
        Id = snapshot.Id;
        Name = snapshot.Name;
        HeadAngle = snapshot.HeadAngle;
        ForksStroke = snapshot.ForkStroke;
        ShockStroke = snapshot.ShockStroke;

        RearSuspensionMode = snapshot.RearSuspension.Kind;
        ApplyRearSuspension(snapshot.RearSuspension, snapshot, imageBytes, image);
        WheelGeometry.ApplySnapshot(snapshot.ToSnapshot());
    }

    private void ApplyRearSuspension(RearSuspensionSpec rearSuspension, BikeDraft snapshot, byte[] imageBytes, Bitmap? image)
    {
        switch (rearSuspension)
        {
            case RearSuspensionSpec.Linkage linkage:
                Chainstay = snapshot.Chainstay;
                PixelsToMillimeters = snapshot.PixelsToMillimeters;
                ImageCanvas.ApplyImage(imageBytes, image, snapshot.ImageRotationDegrees);
                LinkageEditor.Load(linkage.Spec, ImageCanvas.Image?.Size.Height, snapshot.PixelsToMillimeters);
                LeverageRatioEditor.ReplaceState(null);
                break;

            case RearSuspensionSpec.LeverageRatio leverageRatio:
                Chainstay = null;
                PixelsToMillimeters = null;
                ImageCanvas.ApplyImage([], null, 0);
                LinkageEditor.Load(null, null, null);
                LeverageRatioEditor.ReplaceState(leverageRatio.Spec);
                break;

            case RearSuspensionSpec.Hardtail:
            case RearSuspensionSpec.LinkageDraft:
            case RearSuspensionSpec.LeverageRatioDraft:
                Chainstay = null;
                PixelsToMillimeters = null;
                ImageCanvas.ApplyImage([], null, 0);
                LinkageEditor.Load(null, null, null);
                LeverageRatioEditor.ReplaceState(null);
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

    private BikeDraft CaptureDraft()
    {
        var rearSuspension = BuildCurrentRearSuspensionSpec();
        var pixelsToMillimeters = IsLinkageMode ? PixelsToMillimeters ?? draftBase.PixelsToMillimeters : 0;
        return new BikeDraft(
            Id, Name ?? $"bike {Id}", HeadAngle, ForksStroke, ShockStroke, rearSuspension, pixelsToMillimeters,
            WheelSpec.FromValues(WheelGeometry.FrontWheelDiameter, WheelGeometry.FrontWheelRimSize, WheelGeometry.FrontWheelTireWidth),
            WheelSpec.FromValues(WheelGeometry.RearWheelDiameter, WheelGeometry.RearWheelRimSize, WheelGeometry.RearWheelTireWidth),
            IsLinkageMode ? ImageCanvas.ImageRotationDegrees : 0, IsLinkageMode ? ImageCanvas.ImageBytes : [])
        {
            InitialDampingSpeedCutoffs = draftBase.InitialDampingSpeedCutoffs,
        };
    }

    private RearSuspensionSpec BuildCurrentRearSuspensionSpec() => RearSuspensionMode switch
    {
        RearSuspensionKind.None => new RearSuspensionSpec.Hardtail(),
        RearSuspensionKind.Linkage when CreateCurrentLinkageSpec() is LinkageSpec linkage => new RearSuspensionSpec.Linkage(linkage),
        RearSuspensionKind.Linkage => new RearSuspensionSpec.LinkageDraft(),
        RearSuspensionKind.LeverageRatio when LeverageRatioEditor.BuildCurrent() is LeverageRatioSpec leverageRatio => new RearSuspensionSpec.LeverageRatio(leverageRatio),
        RearSuspensionKind.LeverageRatio => new RearSuspensionSpec.LeverageRatioDraft(),
        _ => throw new ArgumentOutOfRangeException(nameof(RearSuspensionMode)),
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
        if (ImageCanvas.Image is null) return;

        var headTube1 = LinkageEditor.JointViewModels.FirstOrDefault(joint => joint.Name == JointNames.HeadTube1);
        var headTube2 = LinkageEditor.JointViewModels.FirstOrDefault(joint => joint.Name == JointNames.HeadTube2);
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

    private void QueuePlotRefresh(bool showPlotBusyOverlay = true) =>
        _ = RefreshAnalysisAsync(showPlotBusyOverlay, debounce: true);

    private Task RefreshAnalysisAsync(bool showPlotBusyOverlay = false, bool debounce = false)
    {
        var spec = BuildCurrentRearSuspensionSpec();
        if (spec == analysisSpec) return analysisTask;

        var token = analysisOperation.Start();
        analysisSpec = spec;
        IsPlotBusy = showPlotBusyOverlay;
        analysisTask = ComputeAnalysisAsync(spec, debounce, token);
        return analysisTask;
    }

    private async Task ComputeAnalysisAsync(RearSuspensionSpec spec, bool debounce, CancellationToken token)
    {
        try
        {
            if (debounce) await Task.Delay(TimeSpan.FromMilliseconds(200), Clock, token);
            var result = await bikeCoordinator.LoadAnalysisAsync(spec, token);
            if (token.IsCancellationRequested) return;

            ApplyAnalysisResult(result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            if (token.IsCancellationRequested) return;
            Log.ForContext<BikeEditorViewModel>().Error(e, "Bike analysis could not be presented for {BikeId}", Id);
            ApplyAnalysisResult(new BikeEditorAnalysisResult.Failed(BikeAnalysisFailureReason.Analyze));
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
            case BikeEditorAnalysisResult.NotApplicable:
            case BikeEditorAnalysisResult.Unsolvable:
                LeverageRatioData = null;
                ImageCanvas.SetRearAxlePathData(null);
                ImageCanvas.RefreshRearAxlePath(PixelsToMillimeters);
                break;
            case BikeEditorAnalysisResult.Failed failed:
                LeverageRatioData = null;
                ImageCanvas.SetRearAxlePathData(null);
                ImageCanvas.RefreshRearAxlePath(PixelsToMillimeters);
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    private void NotifyEditorCommandStatesChanged()
    {
        SaveCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        ImportLeverageRatioCommand.NotifyCanExecuteChanged();
        OpenImageCommand.NotifyCanExecuteChanged();
        SetRearSuspensionModeCommand.NotifyCanExecuteChanged();
    }

    private void RefreshLinkagePreview()
    {
        UpdatePixelsToMillimeters();
        WheelGeometry.RefreshDerived(GetFrontWheelCenter(), GetRearWheelCenter(), PixelsToMillimeters);
        ImageCanvas.RefreshLayout(LinkageEditor.GetJointBounds(), WheelGeometry.GetWheelBounds());
        RecalculateHeadAngle();
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
    }

    internal void HandleLinkagePreviewChangedForTests(LinkagePreviewChangedEventArgs args)
    {
        HandleLinkagePreviewChanged(args);
    }

    private void OnLinkageEditorPreviewChanged(object? sender, LinkagePreviewChangedEventArgs e)
    {
        HandleLinkagePreviewChanged(e);
        if (!IsReplacingState && IsLinkageMode) QueuePlotRefresh();
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
        QueuePlotRefresh();
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

        EvaluateDirtiness();
        NotifyEditorCommandStatesChanged();
        QueuePlotRefresh(showPlotBusyOverlay: false);
    }

    private bool HasOutgoingPayload(RearSuspensionKind mode)
    {
        return mode switch
        {
            RearSuspensionKind.None => false,
            RearSuspensionKind.Linkage =>
                CreateCurrentLinkageSpec() is not null ||
                ImageCanvas.Image is not null ||
                Chainstay is not null,
            RearSuspensionKind.LeverageRatio =>
                LeverageRatioEditor.BuildCurrent() is not null,
            _ => false,
        };
    }

    private void ResetOutgoingPayload(RearSuspensionKind mode)
    {
        switch (mode)
        {
            case RearSuspensionKind.None:
                return;

            case RearSuspensionKind.Linkage:
                Chainstay = null;
                PixelsToMillimeters = null;
                WheelGeometry.ClearAll();
                ImageCanvas.ApplyImage([], null, 0);
                ImageCanvas.SetRearAxlePathData(null);
                LinkageEditor.Load(null, null, null);
                break;

            case RearSuspensionKind.LeverageRatio:
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

    private static string ModeLabel(RearSuspensionKind mode) => mode switch
    {
        RearSuspensionKind.None => "hardtail",
        RearSuspensionKind.Linkage => "linkage",
        RearSuspensionKind.LeverageRatio => "leverage ratio",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

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
        if (!draftReady)
        {
            IsDirty = !IsInDatabase;
            return;
        }
        var acceptedSnapshot = draftBase;
        var acceptedMode = acceptedSnapshot.RearSuspension.Kind;
        var acceptedLinkage = (acceptedSnapshot.RearSuspension as RearSuspensionSpec.Linkage)?.Spec;
        var acceptedLeverageRatio = (acceptedSnapshot.RearSuspension as RearSuspensionSpec.LeverageRatio)?.Spec;
        var linkageDirty = IsLinkageMode &&
            (!MathUtils.AreEqual(Chainstay, acceptedMode == RearSuspensionKind.Linkage ? acceptedSnapshot.Chainstay : null) ||
             LinkageEditor.HasChangesComparedTo(
                 acceptedMode == RearSuspensionKind.Linkage ? acceptedLinkage : null,
                 ImageCanvas.Image?.Size.Height,
                 PixelsToMillimeters) ||
             (acceptedMode == RearSuspensionKind.Linkage
                 ? ImageCanvas.HasChangesComparedTo(acceptedSnapshot.ImageBytesMemory, acceptedSnapshot.ImageRotationDegrees)
                 : ImageCanvas.Image is not null || !MathUtils.AreEqual(ImageCanvas.ImageRotationDegrees, 0)));
        var leverageRatioDirty = IsLeverageRatioMode &&
            LeverageRatioEditor.BuildCurrent() !=
                (acceptedMode == RearSuspensionKind.LeverageRatio ? acceptedLeverageRatio : null);

        IsDirty =
            !IsInDatabase ||
            Name != acceptedSnapshot.Name ||
            !MathUtils.AreEqual(HeadAngle, acceptedSnapshot.HeadAngle) ||
            !MathUtils.AreEqual(ForksStroke, acceptedSnapshot.ForkStroke) ||
            !MathUtils.AreEqual(ShockStroke, acceptedSnapshot.ShockStroke) ||
            RearSuspensionMode != acceptedMode ||
            linkageDirty ||
            leverageRatioDirty ||
            WheelGeometry.HasChangesComparedTo(acceptedSnapshot.FrontWheel, acceptedSnapshot.RearWheel);
    }

    protected override bool CanExport() => draftReady && !IsLoadingDraft && base.CanExport();

    protected override bool CanReset() => draftReady && !IsLoadingDraft && base.CanReset();

    partial void OnIsLoadingDraftChanged(bool value) => NotifyEditorCommandStatesChanged();

    protected override bool CanSave()
    {
        EvaluateDirtiness();

        return draftReady && !IsLoadingDraft && IsDirty && BikeSaveValidator.HasRequiredInputs(HeadAngle, ForksStroke, RearSuspensionMode,
            WheelGeometry.FrontWheelDiameter, WheelGeometry.RearWheelDiameter);
    }

    protected override async Task SaveImplementation()
    {
        if (IsLinkageMode)
        {
            RecalculateGroundRotation();
        }

        var draft = CaptureDraft();
        var result = await bikeCoordinator.SaveAsync(draft, IsInDatabase ? draftBase : null);

        switch (result)
        {
            case BikeSaveResult.Saved saved:
                AcceptBaseline(BikeDraft.From(saved.Snapshot, draft.ImageBytes));
                IsInDatabase = true;

                EvaluateDirtiness();

                NotifyDeleteCommandStateChanged();
                if (!IsDirty) shell.CompleteEdit(new EditorKey.Bike(saved.Snapshot.Id));
                break;

            case BikeSaveResult.Conflict conflict:
                var reload = await dialogService.ShowConfirmationAsync(
                    "Bike changed elsewhere",
                    "This bike has been updated from another source. Discard your changes and reload?");
                if (reload)
                {
                    IsInDatabase = true;
                    stateLoadTask = ReplaceStateAsync(conflict.CurrentDraft, refreshAnalysis: true, showPlotBusyOverlay: true);
                    await stateLoadTask;
                    NotifyDeleteCommandStateChanged();
                }
                break;

            case BikeSaveResult.Missing:
                Notifications.Add("The bike is no longer available.");
                break;

            case BikeSaveResult.Invalid invalidRearSuspension:
                ErrorMessages.Add(new FailurePresentation(invalidRearSuspension.ErrorMessage));
                break;

            case BikeSaveResult.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    protected override async Task CloseImplementation()
    {
        stateOperation.Cancel();
        Unloaded();
        await stateLoadTask;
        if (OpenImageCommand.ExecutionTask is { } imageTask) await imageTask;
        draftReady = false;
        IsLoadingDraft = false;
        ImageCanvas.Dispose();
    }

    protected override bool CanDelete() =>
        !IsInDatabase || !dependencyQuery.IsBikeInUse(Id);

    protected override async Task DeleteImplementation(bool navigateBack)
    {
        if (!IsInDatabase)
        {
            await shell.CloseEditorAsync(new EditorKey.Bike(Id));
            return;
        }

        var result = await bikeCoordinator.DeleteAsync(Id);
        switch (result)
        {
            case BikeDeleteResult.Deleted:
                await shell.CloseEditorAsync(new EditorKey.Bike(Id));
                break;
            case BikeDeleteResult.InUse:
                ErrorMessages.Add(new FailurePresentation("Bike is referenced by a setup and cannot be deleted."));
                break;
            case BikeDeleteResult.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    protected override async Task ResetImplementation()
    {
        stateLoadTask = ReplaceStateAsync(draftBase, acceptBaseline: false);
        await stateLoadTask;
        if (draftReady) await RefreshAnalysisAsync(showPlotBusyOverlay: true);
    }

    protected override async Task ExportImplementation()
    {
        var result = await bikeCoordinator.ExportBikeAsync(CaptureDraft());
        if (result is BikeExportResult.Failed failed)
        {
            ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
        }
    }

    #endregion TabPageViewModelBase overrides

    #region Commands

    private bool CanSetRearSuspensionMode() => CanChangeRearSuspensionMode && draftReady && !IsLoadingDraft;

    [RelayCommand(CanExecute = nameof(CanSetRearSuspensionMode))]
    private async Task SetRearSuspensionMode(RearSuspensionKind newValue)
    {
        var oldValue = RearSuspensionMode;
        if (!CanSetRearSuspensionMode() || oldValue == newValue)
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
                return;
            }
        }

        RearSuspensionMode = newValue;
        ResetOutgoingPayload(oldValue);

        if (newValue == RearSuspensionKind.None)
        {
            ShockStroke = null;
        }

        if (newValue == RearSuspensionKind.Linkage)
        {
            EnsureLinkageSeeded();
        }

        EvaluateDirtiness();
        NotifyEditorCommandStatesChanged();
        QueuePlotRefresh(showPlotBusyOverlay: false);
    }

    private bool CanOpenImage() => !IsLoadingDraft && (draftReady || !draftBase.ImageBytesMemory.IsEmpty);

    [RelayCommand(CanExecute = nameof(CanOpenImage))]
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
                    try
                    {
                        var image = await backgroundTaskRunner.RunAsync(() => BikeImageData.Decode(loaded.ImageBytes)
                            ?? throw new InvalidOperationException("Bike image could not be decoded."), token);
                        if (token.IsCancellationRequested)
                        {
                            image.Dispose();
                            return;
                        }
                        if (draftReady)
                            ImageCanvas.ApplyImage(loaded.ImageBytes, image, ImageCanvas.ImageRotationDegrees);
                        else
                        {
                            RunStateReplacement(() => ApplyRawBikeState(draftBase, loaded.ImageBytes, image));
                            draftReady = true;
                            EvaluateDirtiness();
                            NotifyEditorCommandStatesChanged();
                            QueuePlotRefresh();
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                    catch (Exception e)
                    {
                        Log.ForContext<BikeEditorViewModel>().Warning(e, "Selected bike image could not be decoded for {BikeId}", Id);
                        ErrorMessages.Add(FailurePresentation.From(BikeImageFailureReason.Decode));
                    }
                    break;
                case BikeImageLoadResult.Failed failed:
                    ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
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

            if (result is LeverageRatioImportResult.Failed failed)
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
            else
                LeverageRatioEditor.ApplyImportResult(result);
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

        await stateLoadTask;
        if (draftReady) await RefreshAnalysisAsync();
    }

    [RelayCommand]
    private void Unloaded()
    {
        DisposeScopedSubscriptions();
        analysisOperation.Cancel();
        analysisSpec = null;
        IsPlotBusy = false;
        imageOperation.Cancel();
        leverageRatioImportOperation.Cancel();
    }

    #endregion Commands
}
