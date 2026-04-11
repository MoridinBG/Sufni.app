using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Sufni.App.BikeEditing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels.LinkageEditing;
using Sufni.App.ViewModels.LinkageParts;
using Sufni.Kinematics;

namespace Sufni.App.ViewModels.Editors;

public sealed record RimSizeOption(EtrtoRimSize Value, string DisplayName);

/// <summary>
/// Editor view model for a bike. Created by <c>BikeCoordinator</c>
/// from a <see cref="BikeSnapshot"/>; the snapshot's <c>Updated</c>
/// value is kept as <see cref="BaselineUpdated"/> for optimistic
/// conflict detection at save time.
/// </summary>
public partial class BikeEditorViewModel : TabPageViewModelBase, IEditorActions
{
    public Guid Id { get; private set; }
    public long BaselineUpdated { get; private set; }
    public bool IsInDatabase { get; private set; }

    // Explicit interface implementation: the generated commands are
    // IAsyncRelayCommand[<T>] which C# does not implicitly satisfy a
    // non-generic IRelayCommand interface property with.
    IRelayCommand IEditorActions.OpenPreviousPageCommand => OpenPreviousPageCommand;
    IRelayCommand IEditorActions.SaveCommand => SaveCommand;
    IRelayCommand IEditorActions.ResetCommand => ResetCommand;
    IRelayCommand IEditorActions.DeleteCommand => DeleteCommand;
    IRelayCommand IEditorActions.FakeDeleteCommand => FakeDeleteCommand;

    #region Private fields

    private readonly IBikeCoordinator? bikeCoordinator;
    private readonly IBikeDependencyQuery? dependencyQuery;
    // Immutable editor baseline used for dirty checks and reset/conflict reload.
    private BikeSnapshot acceptedSnapshot;
    private readonly CancellableOperation analysisOperation = new();
    private readonly CancellableOperation imageOperation = new();
    private readonly CancellableOperation importOperation = new();
    // Guard edit-time callbacks while a snapshot is being applied.
    private bool isReplacingState;

    #endregion Private fields

    #region Bike geometry properties

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private double? headAngle;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private double? forksStroke;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private double? shockStroke;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private double? chainstay;

    [ObservableProperty] private double? pixelsToMillimeters;

    #endregion Bike geometry properties

    #region Image properties

    public BikeImageCanvasViewModel ImageCanvas { get; } = new();

    #endregion Image properties

    #region Wheel properties

    public BikeWheelGeometryViewModel WheelGeometry { get; } = new();

    #endregion Wheel properties

    #region Linkage editor properties

    public LinkageEditorViewModel LinkageEditor { get; } = new();

    #endregion Linkage editor properties

    #region Analysis properties

    [ObservableProperty] private CoordinateList? leverageRatioData;
    [ObservableProperty] private bool isPlotBusy;

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
    }

    partial void OnPixelsToMillimetersChanged(double? value)
    {
        if (IsReplacingState) return;

        LinkageEditor.SetPixelsToMillimeters(PixelsToMillimeters);
        WheelGeometry.RefreshDerived(GetFrontWheelCenter(), GetRearWheelCenter(), PixelsToMillimeters);
        RecalculateHeadAngle();
        ImageCanvas.RefreshRearAxlePath(PixelsToMillimeters);
    }

    #endregion Property change handlers

    #region Constructors

    public BikeEditorViewModel()
    {
        bikeCoordinator = null;
        dependencyQuery = null;
        acceptedSnapshot = BikeSnapshot.From(new Bike(Guid.Empty, string.Empty));
        IsInDatabase = false;
        LinkageEditor.Changed += OnLinkageEditorChanged;
        WheelGeometry.PropertyChanged += OnWheelGeometryPropertyChanged;
        ImageCanvas.PropertyChanged += OnImageCanvasPropertyChanged;
    }

    public BikeEditorViewModel(
        BikeSnapshot snapshot,
        bool isNew,
        IBikeCoordinator bikeCoordinator,
        IBikeDependencyQuery dependencyQuery,
        IShellCoordinator shell,
        IDialogService dialogService)
        : base(shell, dialogService)
    {
        this.bikeCoordinator = bikeCoordinator;
        this.dependencyQuery = dependencyQuery;
        IsInDatabase = !isNew;
        acceptedSnapshot = snapshot;
        LinkageEditor.Changed += OnLinkageEditorChanged;
        WheelGeometry.PropertyChanged += OnWheelGeometryPropertyChanged;
        ImageCanvas.PropertyChanged += OnImageCanvasPropertyChanged;

        ReplaceState(snapshot, refreshAnalysis: !isNew);

        // Brand new bike: start with the mandatory joints.
        if (isNew) LinkageEditor.AddInitialJoints();
    }

    #endregion Constructors

    #region Private methods

    private bool IsReplacingState => isReplacingState;

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
        Chainstay = snapshot.Chainstay;
        PixelsToMillimeters = snapshot.Linkage is null ? null : snapshot.PixelsToMillimeters;
        ImageCanvas.ApplySnapshot(snapshot.Image, snapshot.ImageRotationDegrees);
        LinkageEditor.Load(snapshot.Linkage, snapshot.Image?.Size.Height, snapshot.Linkage is null ? null : snapshot.PixelsToMillimeters);
        WheelGeometry.ApplySnapshot(snapshot);
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

        var linkage = CreateCurrentLinkage();
        var pixelsToMillimeters = linkage is null
            ? acceptedSnapshot.PixelsToMillimeters
            : PixelsToMillimeters ?? acceptedSnapshot.PixelsToMillimeters;

        return new BikeSnapshot(
            Id,
            Name ?? $"bike {Id}",
            HeadAngle.Value,
            ForksStroke,
            ShockStroke,
            Chainstay,
            pixelsToMillimeters,
            WheelGeometry.FrontWheelDiameter,
            WheelGeometry.RearWheelDiameter,
            WheelGeometry.FrontWheelRimSize,
            WheelGeometry.FrontWheelTireWidth,
            WheelGeometry.RearWheelRimSize,
            WheelGeometry.RearWheelTireWidth,
            ImageCanvas.ImageRotationDegrees,
            linkage,
            ImageCanvas.Image,
            updated);
    }

    private Linkage? CreateCurrentLinkage()
    {
        return LinkageEditor.BuildCurrentLinkage(ImageCanvas.Image?.Size.Height, PixelsToMillimeters, ShockStroke);
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

        var distance = GeometryUtils.CalculateDistance(rw, bb);
        PixelsToMillimeters = Chainstay / distance;
    }

    private void RecalculateHeadAngle()
    {
        if (PixelsToMillimeters is null || !WheelGeometry.FrontWheelDiameter.HasValue || !WheelGeometry.RearWheelDiameter.HasValue) return;

        var headTubeJoints = LinkageEditor.JointViewModels.Where(joint => joint.Type == JointType.HeadTube).ToList();
        var frontWheel = GetFrontWheelJoint();
        var rearWheel = GetRearWheelJoint();

        if (headTubeJoints.Count < 2 || frontWheel is null || rearWheel is null) return;

        var headTube1 = headTubeJoints[0];
        var headTube2 = headTubeJoints[1];

        var frontRadiusPixels = WheelGeometry.FrontWheelDiameter.Value / 2.0 / PixelsToMillimeters.Value;
        var rearRadiusPixels = WheelGeometry.RearWheelDiameter.Value / 2.0 / PixelsToMillimeters.Value;

        var frontContactY = frontWheel.Y + frontRadiusPixels;
        var rearContactY = rearWheel.Y + rearRadiusPixels;

        var dxGround = frontWheel.X - rearWheel.X;
        var dyGround = frontContactY - rearContactY;

        var top = headTube1.Y < headTube2.Y ? headTube1 : headTube2;
        var bottom = headTube1.Y < headTube2.Y ? headTube2 : headTube1;

        var dxHeadTube = top.X - bottom.X;
        var dyHeadTube = top.Y - bottom.Y;

        var magnitudeGround = Math.Sqrt(dxGround * dxGround + dyGround * dyGround);
        var magnitudeHeadTube = Math.Sqrt(dxHeadTube * dxHeadTube + dyHeadTube * dyHeadTube);
        if (magnitudeGround < 0.001 || magnitudeHeadTube < 0.001) return;

        var dot = dxGround * dxHeadTube + dyGround * dyHeadTube;
        var cos = Math.Clamp(dot / (magnitudeGround * magnitudeHeadTube), -1.0, 1.0);
        var angle = Math.Acos(cos) * 180.0 / Math.PI;

        HeadAngle = Math.Round(180.0 - angle, 1);
    }

    private void QueuePlotRefresh(bool showPlotBusyOverlay = true) => _ = RefreshAnalysisAsync(showPlotBusyOverlay);

    private async Task RefreshAnalysisAsync(bool showPlotBusyOverlay = false)
    {
        if (bikeCoordinator is null) return;

        var token = analysisOperation.Start();
        IsPlotBusy = showPlotBusyOverlay;
        try
        {
            var result = await bikeCoordinator.LoadAnalysisAsync(CreateCurrentLinkage(), token);
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
                ErrorMessages.Add($"Linkage analysis failed: {failed.ErrorMessage}");
                break;
        }
    }

    private void ApplyImportedBike(ImportedBikeEditorData data)
    {
        var importedSnapshot = BikeSnapshot.From(data.Bike);
        IsInDatabase = false;

        ReplaceState(importedSnapshot);
        ApplyAnalysisResult(data.AnalysisResult);

        DeleteCommand.NotifyCanExecuteChanged();
        FakeDeleteCommand.NotifyCanExecuteChanged();
    }

    private void NotifyEditorCommandStatesChanged()
    {
        SaveCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    private void OnLinkageEditorChanged(object? sender, LinkageEditorChange change)
    {
        if (IsReplacingState) return;

        switch (change.Kind)
        {
            case LinkageEditorChangeKind.JointStructureChanged:
                EvaluateDirtiness();
                NotifyEditorCommandStatesChanged();
                LinkageEditor.SetPixelsToMillimeters(PixelsToMillimeters);
                WheelGeometry.RefreshDerived(GetFrontWheelCenter(), GetRearWheelCenter(), PixelsToMillimeters);
                ImageCanvas.RefreshLayout(LinkageEditor.GetJointBounds(), WheelGeometry.GetWheelBounds());
                RecalculateHeadAngle();
                break;

            case LinkageEditorChangeKind.LinkStructureChanged:
                EvaluateDirtiness();
                NotifyEditorCommandStatesChanged();
                LinkageEditor.SetPixelsToMillimeters(PixelsToMillimeters);
                break;

            case LinkageEditorChangeKind.JointMetadataChanged:
                EvaluateDirtiness();
                NotifyEditorCommandStatesChanged();
                if (change.Joint?.Type == JointType.HeadTube)
                {
                    RecalculateHeadAngle();
                }
                break;

            case LinkageEditorChangeKind.JointCoordinatesChanged:
                if (change.Joint?.Type is JointType.BottomBracket or JointType.RearWheel)
                {
                    UpdatePixelsToMillimeters();
                }

                if (change.Joint?.Type is JointType.FrontWheel or JointType.RearWheel)
                {
                    WheelGeometry.RefreshDerived(GetFrontWheelCenter(), GetRearWheelCenter(), PixelsToMillimeters);
                }

                ImageCanvas.RefreshLayout(LinkageEditor.GetJointBounds(), WheelGeometry.GetWheelBounds());
                RecalculateHeadAngle();
                break;

            case LinkageEditorChangeKind.LinkEndpointsChanged:
                EvaluateDirtiness();
                NotifyEditorCommandStatesChanged();
                break;

            case LinkageEditorChangeKind.DragCompleted:
                EvaluateDirtiness();
                NotifyEditorCommandStatesChanged();
                break;

            case LinkageEditorChangeKind.SelectionChanged:
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
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
        IsDirty =
            !IsInDatabase ||
            Name != acceptedSnapshot.Name ||
            !MathUtils.AreEqual(HeadAngle, acceptedSnapshot.HeadAngle) ||
            !MathUtils.AreEqual(ForksStroke, acceptedSnapshot.ForkStroke) ||
            !MathUtils.AreEqual(ShockStroke, acceptedSnapshot.ShockStroke) ||
            !MathUtils.AreEqual(Chainstay, acceptedSnapshot.Chainstay) ||
            LinkageEditor.HasChangesComparedTo(acceptedSnapshot.Linkage, ImageCanvas.Image?.Size.Height, PixelsToMillimeters) ||
            WheelGeometry.HasChangesComparedTo(acceptedSnapshot) ||
            ImageCanvas.HasChangesComparedTo(acceptedSnapshot);
    }

    protected override bool CanSave()
    {
        EvaluateDirtiness();

        var frontHasDiameter = WheelGeometry.FrontWheelDiameter.HasValue;
        var rearHasDiameter = WheelGeometry.RearWheelDiameter.HasValue;
        var wheelsValid = frontHasDiameter == rearHasDiameter;

        return IsDirty &&
               HeadAngle is not null &&
               ForksStroke is not null &&
             (ShockStroke is null || (ImageCanvas.Image is not null && Chainstay is not null)) &&
               wheelsValid;
    }

    protected override async Task SaveImplementation()
    {
        if (bikeCoordinator is null) return;

        RecalculateGroundRotation();
        var snapshot = ToSnapshot(BaselineUpdated);
        var bike = Bike.FromSnapshot(snapshot);
        var result = await bikeCoordinator.SaveAsync(bike, BaselineUpdated);

        switch (result)
        {
            case BikeSaveResult.Saved saved:
                bike.Updated = saved.NewBaselineUpdated;
                AcceptBaseline(BikeSnapshot.From(bike));
                IsInDatabase = true;

                ApplyAnalysisResult(saved.AnalysisResult);
                EvaluateDirtiness();

                DeleteCommand.NotifyCanExecuteChanged();
                FakeDeleteCommand.NotifyCanExecuteChanged();

                Debug.Assert(App.Current is not null);
                if (!App.Current.IsDesktop)
                {
                    OpenPreviousPage();
                }
                break;

            case BikeSaveResult.Conflict conflict:
                var reload = await dialogService.ShowConfirmationAsync(
                    "Bike changed elsewhere",
                    "This bike has been updated from another source. Discard your changes and reload?");
                if (reload)
                {
                    IsInDatabase = true;
                    ReplaceState(conflict.CurrentSnapshot, refreshAnalysis: true, showPlotBusyOverlay: true);
                    DeleteCommand.NotifyCanExecuteChanged();
                    FakeDeleteCommand.NotifyCanExecuteChanged();
                }
                break;

            case BikeSaveResult.InvalidLinkage:
                ErrorMessages.Add("Linkage movement could not be calculated. Please check the joints and links!");
                break;

            case BikeSaveResult.Failed failed:
                ErrorMessages.Add($"Bike could not be saved: {failed.ErrorMessage}");
                break;
        }
    }

    private bool CanDelete() =>
        !IsInDatabase || dependencyQuery is null || !dependencyQuery.IsBikeInUse(Id);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete(bool navigateBack)
    {
        if (bikeCoordinator is null) return;

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

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void FakeDelete()
    {
        // Exists so the editor button strip can bind to a delete command.
    }

    protected override Task ResetImplementation()
    {
        ReplaceState(acceptedSnapshot, acceptBaseline: false);
        return RefreshAnalysisAsync(showPlotBusyOverlay: true);
    }

    protected override async Task ExportImplementation()
    {
        if (bikeCoordinator is null) return;

        var result = await bikeCoordinator.ExportBikeAsync(Bike.FromSnapshot(ToSnapshot(BaselineUpdated)));
        if (result is BikeExportResult.Failed failed)
        {
            ErrorMessages.Add($"Bike could not be exported: {failed.ErrorMessage}");
        }
    }

    #endregion TabPageViewModelBase overrides

    #region Commands

    [RelayCommand]
    private async Task OpenImage()
    {
        if (bikeCoordinator is null) return;

        var token = imageOperation.Start();
        try
        {
            var result = await bikeCoordinator.LoadImageAsync(token);
            if (token.IsCancellationRequested) return;

            switch (result)
            {
                case BikeImageLoadResult.Loaded loaded:
                    ImageCanvas.Image = loaded.Bitmap;
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

    [RelayCommand]
    private async Task Import()
    {
        if (bikeCoordinator is null) return;

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
        if (dependencyQuery is not null)
        {
            EnsureScopedSubscription(s => s.Add(dependencyQuery.Changes.Subscribe(_ =>
            {
                DeleteCommand.NotifyCanExecuteChanged();
                FakeDeleteCommand.NotifyCanExecuteChanged();
            })));
        }

        await RefreshAnalysisAsync();
    }

    [RelayCommand]
    private void Unloaded()
    {
        DisposeScopedSubscriptions();
        analysisOperation.Cancel();
        imageOperation.Cancel();
        importOperation.Cancel();
    }

    #endregion Commands
}
