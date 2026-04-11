using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Sufni.App.BikeEditing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Stores;
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
    private uint pointNumber = 1;
    private LinkViewModel? shockViewModel;
    private readonly CancellableOperation analysisOperation = new();
    private readonly CancellableOperation imageOperation = new();
    private readonly CancellableOperation importOperation = new();
    private readonly Dictionary<JointViewModel, PropertyChangedEventHandler> jointPropertyChangedHandlers = [];
    private readonly Dictionary<LinkViewModel, PropertyChangedEventHandler> linkPropertyChangedHandlers = [];
    private CoordinateList? rearAxlePathData;
    private Bitmap? rotatedImageCache;
    private double rotatedImageCacheAngle = double.NaN;
    private bool suppressWheelStateCallbacks;
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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private Bitmap? image;

    [ObservableProperty] private double imageRotationDegrees;

    public Bitmap? RotatedImage
    {
        get
        {
            if (Image is null) return null;
            if (Math.Abs(ImageRotationDegrees) < 0.01) return Image;

            if (rotatedImageCache is not null && Math.Abs(rotatedImageCacheAngle - ImageRotationDegrees) < 0.001)
            {
                return rotatedImageCache;
            }

            rotatedImageCache = CreateRotatedBitmap(Image, ImageRotationDegrees);
            rotatedImageCacheAngle = ImageRotationDegrees;
            return rotatedImageCache;
        }
    }

    public double RotatedImageLeft
    {
        get
        {
            if (Image is null || Math.Abs(ImageRotationDegrees) < 0.01) return 0;
            var bounds = CoordinateRotation.GetRotatedBounds(Image.Size.Width, Image.Size.Height, ImageRotationDegrees);
            return bounds.minX;
        }
    }

    public double RotatedImageTop
    {
        get
        {
            if (Image is null || Math.Abs(ImageRotationDegrees) < 0.01) return 0;
            var bounds = CoordinateRotation.GetRotatedBounds(Image.Size.Width, Image.Size.Height, ImageRotationDegrees);
            return bounds.minY;
        }
    }

    #endregion Image properties

    #region Wheel properties

    public static RimSizeOption[] RimSizeOptions { get; } = Enum.GetValues<EtrtoRimSize>()
        .Select(rimSize => new RimSizeOption(rimSize, rimSize.DisplayName))
        .ToArray();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private EtrtoRimSize? frontWheelRimSize;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private double? frontWheelTireWidth;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private double? frontWheelDiameter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private EtrtoRimSize? rearWheelRimSize;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private double? rearWheelTireWidth;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private double? rearWheelDiameter;

    public bool HasWheels =>
        FrontWheelDiameter.HasValue &&
        RearWheelDiameter.HasValue &&
        GetFrontWheelJoint() is not null &&
        GetRearWheelJoint() is not null;

    public double? FrontWheelRadiusPixels => FrontWheelDiameter / 2.0 / PixelsToMillimeters;
    public double? RearWheelRadiusPixels => RearWheelDiameter / 2.0 / PixelsToMillimeters;

    public double FrontWheelCircleLeft => GetFrontWheelJoint()?.X - (FrontWheelRadiusPixels ?? 0) ?? 0;
    public double FrontWheelCircleTop => GetFrontWheelJoint()?.Y - (FrontWheelRadiusPixels ?? 0) ?? 0;
    public double FrontWheelCircleDiameter => (FrontWheelRadiusPixels ?? 0) * 2;

    public double RearWheelCircleLeft => GetRearWheelJoint()?.X - (RearWheelRadiusPixels ?? 0) ?? 0;
    public double RearWheelCircleTop => GetRearWheelJoint()?.Y - (RearWheelRadiusPixels ?? 0) ?? 0;
    public double RearWheelCircleDiameter => (RearWheelRadiusPixels ?? 0) * 2;

    public double FrontRimCircleDiameter => ComputeRimCircleDiameter(FrontWheelRimSize, FrontWheelDiameter);
    public double RearRimCircleDiameter => ComputeRimCircleDiameter(RearWheelRimSize, RearWheelDiameter);

    public double FrontTireThickness => (FrontWheelCircleDiameter - FrontRimCircleDiameter) / 2;
    public double RearTireThickness => (RearWheelCircleDiameter - RearRimCircleDiameter) / 2;

    public double FrontHubDiameter => FrontWheelCircleDiameter * 0.08;
    public double RearHubDiameter => RearWheelCircleDiameter * 0.08;

    public string FrontWheelDisplayText => FormatWheelDisplay(FrontWheelRimSize, FrontWheelTireWidth, FrontWheelDiameter);
    public string RearWheelDisplayText => FormatWheelDisplay(RearWheelRimSize, RearWheelTireWidth, RearWheelDiameter);

    #endregion Wheel properties

    #region Linkage editor properties

    public ObservableCollection<JointViewModel> JointViewModels { get; } = [];
    public ObservableCollection<LinkViewModel> LinkViewModels { get; } = [];
    [ObservableProperty] private JointViewModel? selectedPoint;
    [ObservableProperty] private LinkViewModel? selectedLink;
    [ObservableProperty] private bool overlayVisible;

    #endregion Linkage editor properties

    #region Analysis properties

    [ObservableProperty] private CoordinateList? leverageRatioData;
    [ObservableProperty] private List<Point> rearAxlePath = [];
    [ObservableProperty] private bool isPlotBusy;

    #endregion Analysis properties

    #region Canvas layout

    private const double CanvasPadding = 20;

    public double CanvasWidth => ContentMaxX - ContentMinX + 2 * CanvasPadding;
    public double CanvasHeight => ContentMaxY - ContentMinY + 2 * CanvasPadding;
    public double ContentOffsetX => -ContentMinX + CanvasPadding;
    public double ContentOffsetY => -ContentMinY + CanvasPadding;

    #endregion Canvas layout

    #region Property change handlers

    partial void OnSelectedLinkChanged(LinkViewModel? value)
    {
        if (SelectedLink is null) return;
        ClearSelections();
        SelectedLink.IsSelected = true;
        SelectedPoint = null;
    }

    partial void OnSelectedPointChanged(JointViewModel? value)
    {
        if (SelectedPoint is null) return;
        ClearSelections();
        SelectedPoint.IsSelected = true;
        SelectedLink = null;
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

        foreach (var link in LinkViewModels)
        {
            link.UpdateLength(PixelsToMillimeters);
        }

        NotifyFrontWheelPropertiesChanged();
        NotifyRearWheelPropertiesChanged();
        RecalculateHeadAngle();
        UpdateRearAxlePathDisplay();
    }

    partial void OnImageChanged(Bitmap? value)
    {
        if (IsReplacingState) return;

        rotatedImageCache = null;
        rotatedImageCacheAngle = double.NaN;

        OnPropertyChanged(nameof(RotatedImage));
        OnPropertyChanged(nameof(RotatedImageLeft));
        OnPropertyChanged(nameof(RotatedImageTop));
        NotifyCanvasBoundsChanged();
        UpdateRearAxlePathDisplay();
    }

    partial void OnImageRotationDegreesChanged(double value)
    {
        if (IsReplacingState) return;

        rotatedImageCache = null;
        rotatedImageCacheAngle = double.NaN;

        OnPropertyChanged(nameof(RotatedImage));
        OnPropertyChanged(nameof(RotatedImageLeft));
        OnPropertyChanged(nameof(RotatedImageTop));
        NotifyCanvasBoundsChanged();
    }

    partial void OnFrontWheelRimSizeChanged(EtrtoRimSize? value)
    {
        if (IsReplacingState) return;
        if (suppressWheelStateCallbacks) return;
        RecalculateFrontWheelDiameter();
        EvaluateDirtiness();
    }

    partial void OnFrontWheelTireWidthChanged(double? value)
    {
        if (IsReplacingState) return;
        if (suppressWheelStateCallbacks) return;
        RecalculateFrontWheelDiameter();
        EvaluateDirtiness();
    }

    partial void OnFrontWheelDiameterChanged(double? value)
    {
        if (IsReplacingState) return;
        if (suppressWheelStateCallbacks) return;

        WithWheelStateCallbacksSuspended(() =>
        {
            FrontWheelRimSize = null;
            FrontWheelTireWidth = null;
        });

        NotifyFrontWheelPropertiesChanged();
        EvaluateDirtiness();
        RecalculateHeadAngle();
        RecalculateGroundRotation();
    }

    partial void OnRearWheelRimSizeChanged(EtrtoRimSize? value)
    {
        if (IsReplacingState) return;
        if (suppressWheelStateCallbacks) return;
        RecalculateRearWheelDiameter();
        EvaluateDirtiness();
    }

    partial void OnRearWheelTireWidthChanged(double? value)
    {
        if (IsReplacingState) return;
        if (suppressWheelStateCallbacks) return;
        RecalculateRearWheelDiameter();
        EvaluateDirtiness();
    }

    partial void OnRearWheelDiameterChanged(double? value)
    {
        if (IsReplacingState) return;
        if (suppressWheelStateCallbacks) return;

        WithWheelStateCallbacksSuspended(() =>
        {
            RearWheelRimSize = null;
            RearWheelTireWidth = null;
        });

        NotifyRearWheelPropertiesChanged();
        EvaluateDirtiness();
        RecalculateHeadAngle();
        RecalculateGroundRotation();
    }

    #endregion Property change handlers

    #region Constructors

    public BikeEditorViewModel()
    {
        bikeCoordinator = null;
        dependencyQuery = null;
        acceptedSnapshot = BikeSnapshot.From(new Bike(Guid.Empty, string.Empty));
        IsInDatabase = false;

        SetupJointsListeners();
        SetupLinksListeners();
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

        SetupJointsListeners();
        SetupLinksListeners();

        ReplaceState(snapshot, refreshAnalysis: !isNew);

        // Brand new bike: start with the mandatory joints.
        if (isNew) AddInitialJoints();
    }

    #endregion Constructors

    #region Private methods

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

        RebuildDerivedDisplayState();
    }

    // Copy persisted inputs verbatim; derived display values are rebuilt afterwards.
    private void ApplyRawBikeState(BikeSnapshot snapshot)
    {
        SelectedLink = null;
        SelectedPoint = null;

        JointViewModels.Clear();
        LinkViewModels.Clear();
        shockViewModel = null;

        Id = snapshot.Id;
        Name = snapshot.Name;
        HeadAngle = snapshot.HeadAngle;
        ForksStroke = snapshot.ForkStroke;
        ShockStroke = snapshot.ShockStroke;
        Image = snapshot.Image;
        Chainstay = snapshot.Chainstay;
        PixelsToMillimeters = snapshot.Linkage is null ? null : snapshot.PixelsToMillimeters;
        ImageRotationDegrees = snapshot.ImageRotationDegrees;

        if (snapshot.Linkage is not null)
        {
            Debug.Assert(snapshot.Image is not null);

            var jointViewModels = snapshot.Linkage.Joints.Select(j => JointViewModel.FromJoint(j, snapshot.Image.Size.Height, snapshot.PixelsToMillimeters));
            foreach (var jvm in jointViewModels)
            {
                JointViewModels.Add(jvm);
            }

            var linkViewModels = snapshot.Linkage.Links.Select(l => LinkViewModel.FromLink(l, JointViewModels));
            foreach (var link in linkViewModels)
            {
                LinkViewModels.Add(link);
            }

            shockViewModel = LinkViewModel.FromLink(snapshot.Linkage.Shock, JointViewModels);
            LinkViewModels.Add(shockViewModel);
        }

        FrontWheelRimSize = snapshot.FrontWheelRimSize;
        FrontWheelTireWidth = snapshot.FrontWheelTireWidth;
        FrontWheelDiameter = snapshot.FrontWheelDiameterMm;
        RearWheelRimSize = snapshot.RearWheelRimSize;
        RearWheelTireWidth = snapshot.RearWheelTireWidth;
        RearWheelDiameter = snapshot.RearWheelDiameterMm;
    }

    // Refresh caches and projections that are safe to recompute from the raw editor state.
    private void RebuildDerivedDisplayState()
    {
        rotatedImageCache = null;
        rotatedImageCacheAngle = double.NaN;

        foreach (var link in LinkViewModels)
        {
            link.UpdateLength(PixelsToMillimeters);
        }

        OnPropertyChanged(nameof(RotatedImage));
        OnPropertyChanged(nameof(RotatedImageLeft));
        OnPropertyChanged(nameof(RotatedImageTop));

        NotifyFrontWheelPropertiesChanged();
        NotifyRearWheelPropertiesChanged();
        NotifyCanvasBoundsChanged();
        UpdateRearAxlePathDisplay();
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
            FrontWheelDiameter,
            RearWheelDiameter,
            FrontWheelRimSize,
            FrontWheelTireWidth,
            RearWheelRimSize,
            RearWheelTireWidth,
            ImageRotationDegrees,
            linkage,
            Image,
            updated);
    }

    private Linkage? CreateCurrentLinkage()
    {
        if (ShockStroke is null || Image is null || PixelsToMillimeters is null || shockViewModel is null)
        {
            return null;
        }

        return CreateLinkage();
    }

    private void AddInitialJoints()
    {
        var mapping = new JointNameMapping();
        JointViewModel EnsureJoint(string name, JointType type, double x, double y)
        {
            var existing = JointViewModels.FirstOrDefault(joint => joint.Name == name);
            if (existing is not null) return existing;

            var jointViewModel = new JointViewModel(name, type, x, y);
            JointViewModels.Add(jointViewModel);
            return jointViewModel;
        }

        EnsureJoint(mapping.FrontWheel, JointType.FrontWheel, 100, 150);
        EnsureJoint(mapping.BottomBracket, JointType.BottomBracket, 100, 200);
        EnsureJoint(mapping.RearWheel, JointType.RearWheel, 100, 100);
        EnsureJoint(mapping.HeadTube1, JointType.HeadTube, 100, 50);
        EnsureJoint(mapping.HeadTube2, JointType.HeadTube, 100, 120);

        var shockEye1 = EnsureJoint(mapping.ShockEye1, JointType.Floating, 100, 250);
        var shockEye2 = EnsureJoint(mapping.ShockEye2, JointType.Floating, 100, 300);
        shockViewModel ??= LinkViewModels.FirstOrDefault(link => link.Name == "Shock");
        if (shockViewModel is null)
        {
            shockViewModel = new LinkViewModel(shockEye1, shockEye2, "Shock");
            LinkViewModels.Add(shockViewModel);
        }
    }

    private JointViewModel? GetFrontWheelJoint() =>
        JointViewModels.FirstOrDefault(joint => joint.Type == JointType.FrontWheel);

    private JointViewModel? GetRearWheelJoint() =>
        JointViewModels.FirstOrDefault(joint => joint.Type == JointType.RearWheel);

    private double ComputeRimCircleDiameter(EtrtoRimSize? rimSize, double? totalDiameter)
    {
        if (!totalDiameter.HasValue || !PixelsToMillimeters.HasValue) return 0;
        var rimDiameterMm = rimSize?.BeadDiameterMm ?? totalDiameter.Value * 0.83;
        return rimDiameterMm / PixelsToMillimeters.Value;
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
        NotifyCanvasBoundsChanged();
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
        NotifyCanvasBoundsChanged();
    }

    private void NotifyWheelJointPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasWheels));
        OnPropertyChanged(nameof(FrontWheelCircleLeft));
        OnPropertyChanged(nameof(FrontWheelCircleTop));
        OnPropertyChanged(nameof(RearWheelCircleLeft));
        OnPropertyChanged(nameof(RearWheelCircleTop));
        NotifyCanvasBoundsChanged();
    }

    private void RecalculateGroundRotation()
    {
        if (!HasWheels || Image is null || !PixelsToMillimeters.HasValue) return;

        var frontWheel = GetFrontWheelJoint();
        var rearWheel = GetRearWheelJoint();
        Debug.Assert(frontWheel is not null && rearWheel is not null);

        var frontRadiusPx = FrontWheelDiameter!.Value / 2.0 / PixelsToMillimeters.Value;
        var rearRadiusPx = RearWheelDiameter!.Value / 2.0 / PixelsToMillimeters.Value;

        var (rotationAngle, _) = GroundCalculator.CalculateGroundRotation(
            frontWheel.X, frontWheel.Y, frontRadiusPx,
            rearWheel.X, rearWheel.Y, rearRadiusPx);

        var newRotation = ImageRotationDegrees + rotationAngle;
        var deltaRotation = newRotation - ImageRotationDegrees;

        if (Math.Abs(deltaRotation) <= 0.01) return;

        CoordinateRotation.RotatePoints(JointViewModels, 0, 0, deltaRotation);

        var items = JointViewModels.ToList();
        JointViewModels.Clear();
        foreach (var item in items)
        {
            JointViewModels.Add(item);
        }

        ImageRotationDegrees = newRotation;
        NotifyWheelJointPropertiesChanged();
    }

    private void UpdatePixelsToMillimeters()
    {
        var bb = JointViewModels.FirstOrDefault(p => p.Type == JointType.BottomBracket);
        var rw = JointViewModels.FirstOrDefault(p => p.Type == JointType.RearWheel);
        if (bb is null || rw is null) return;

        var distance = GeometryUtils.CalculateDistance(rw, bb);
        PixelsToMillimeters = Chainstay / distance;
    }

    private void RecalculateHeadAngle()
    {
        if (PixelsToMillimeters is null || !FrontWheelDiameter.HasValue || !RearWheelDiameter.HasValue) return;

        var mapping = new JointNameMapping();
        var headTube1 = JointViewModels.FirstOrDefault(joint => joint.Name == mapping.HeadTube1);
        var headTube2 = JointViewModels.FirstOrDefault(joint => joint.Name == mapping.HeadTube2);
        var frontWheel = GetFrontWheelJoint();
        var rearWheel = GetRearWheelJoint();

        if (headTube1 is null || headTube2 is null || frontWheel is null || rearWheel is null) return;

        var frontRadiusPixels = FrontWheelDiameter.Value / 2.0 / PixelsToMillimeters.Value;
        var rearRadiusPixels = RearWheelDiameter.Value / 2.0 / PixelsToMillimeters.Value;

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

    private void ClearSelections()
    {
        foreach (var point in JointViewModels)
        {
            point.IsSelected = false;
        }

        foreach (var link in LinkViewModels)
        {
            link.IsSelected = false;
        }
    }

    private static Bitmap CreateRotatedBitmap(Bitmap source, double angleDegrees)
    {
        var bounds = CoordinateRotation.GetRotatedBounds(source.Size.Width, source.Size.Height, angleDegrees);
        var newWidth = (int)Math.Ceiling(bounds.maxX - bounds.minX);
        var newHeight = (int)Math.Ceiling(bounds.maxY - bounds.minY);

        var offsetX = -bounds.minX;
        var offsetY = -bounds.minY;

        var renderTarget = new RenderTargetBitmap(new PixelSize(newWidth, newHeight));
        using var ctx = renderTarget.CreateDrawingContext();
        var translateMatrix = Matrix.CreateTranslation(offsetX, offsetY);
        var rotateMatrix = Matrix.CreateRotation(angleDegrees * Math.PI / 180.0);
        var combinedMatrix = rotateMatrix * translateMatrix;

        using (ctx.PushTransform(combinedMatrix))
        {
            ctx.DrawImage(source, new Rect(0, 0, source.Size.Width, source.Size.Height));
        }

        return renderTarget;
    }

    private Linkage CreateLinkage()
    {
        Debug.Assert(ShockStroke is not null);
        Debug.Assert(Image is not null);
        Debug.Assert(PixelsToMillimeters is not null);
        Debug.Assert(shockViewModel is not null);

        return new Linkage
        {
            Joints = [.. JointViewModels.Select(p => p.ToJoint(Image.Size.Height, PixelsToMillimeters.Value))],
            Links = [.. LinkViewModels.Where(l => l != shockViewModel).Select(l => l.ToLink(Image.Size.Height, PixelsToMillimeters.Value))],
            Shock = shockViewModel.ToLink(Image.Size.Height, PixelsToMillimeters.Value),
            ShockStroke = ShockStroke.Value
        };
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
        RecalculateGroundRotation();
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
        RecalculateGroundRotation();
    }

    private (double minX, double minY, double maxX, double maxY) GetImageBounds()
    {
        if (Image is null) return (0, 0, 0, 0);

        return CoordinateRotation.GetRotatedBounds(
            Image.Size.Width,
            Image.Size.Height,
            ImageRotationDegrees);
    }

    private (double minX, double minY, double maxX, double maxY) GetJointBounds()
    {
        if (JointViewModels.Count == 0)
        {
            return (double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
        }

        const double radius = 20.0;
        var minX = JointViewModels.Min(joint => joint.X) - radius;
        var minY = JointViewModels.Min(joint => joint.Y) - radius;
        var maxX = JointViewModels.Max(joint => joint.X) + radius;
        var maxY = JointViewModels.Max(joint => joint.Y) + radius;

        return (minX, minY, maxX, maxY);
    }

    private double ContentMinX
    {
        get
        {
            var imageBounds = GetImageBounds();
            var jointBounds = GetJointBounds();
            var minX = Math.Min(imageBounds.minX, jointBounds.minX);

            return HasWheels
                ? Math.Min(minX, Math.Min(FrontWheelCircleLeft, RearWheelCircleLeft))
                : minX;
        }
    }

    private double ContentMinY
    {
        get
        {
            var imageBounds = GetImageBounds();
            var jointBounds = GetJointBounds();
            var minY = Math.Min(imageBounds.minY, jointBounds.minY);

            return HasWheels
                ? Math.Min(minY, Math.Min(FrontWheelCircleTop, RearWheelCircleTop))
                : minY;
        }
    }

    private double ContentMaxX
    {
        get
        {
            var imageBounds = GetImageBounds();
            var jointBounds = GetJointBounds();
            var maxX = Math.Max(imageBounds.maxX, jointBounds.maxX);

            return HasWheels
                ? Math.Max(maxX, Math.Max(FrontWheelCircleLeft + FrontWheelCircleDiameter, RearWheelCircleLeft + RearWheelCircleDiameter))
                : maxX;
        }
    }

    private double ContentMaxY
    {
        get
        {
            var imageBounds = GetImageBounds();
            var jointBounds = GetJointBounds();
            var maxY = Math.Max(imageBounds.maxY, jointBounds.maxY);

            return HasWheels
                ? Math.Max(maxY, Math.Max(FrontWheelCircleTop + FrontWheelCircleDiameter, RearWheelCircleTop + RearWheelCircleDiameter))
                : maxY;
        }
    }

    private void NotifyCanvasBoundsChanged()
    {
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        OnPropertyChanged(nameof(ContentOffsetX));
        OnPropertyChanged(nameof(ContentOffsetY));
    }

    private void UpdateRearAxlePathDisplay()
    {
        if (rearAxlePathData is null || Image is null || !PixelsToMillimeters.HasValue)
        {
            RearAxlePath = [];
            return;
        }

        var imageHeight = Image.Size.Height;
        var pixelsToMillimetersValue = PixelsToMillimeters.Value;

        RearAxlePath = rearAxlePathData.Value.X
            .Zip(rearAxlePathData.Value.Y, (x, y) => new Point(
                x / pixelsToMillimetersValue,
                imageHeight - y / pixelsToMillimetersValue))
            .ToList();
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
                rearAxlePathData = computed.Data.RearAxlePathData;
                UpdateRearAxlePathDisplay();
                break;
            case BikeEditorAnalysisResult.Unavailable:
                LeverageRatioData = null;
                rearAxlePathData = null;
                RearAxlePath = [];
                break;
            case BikeEditorAnalysisResult.Failed failed:
                LeverageRatioData = null;
                rearAxlePathData = null;
                RearAxlePath = [];
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

    private void AttachJointPropertyChangedHandler(JointViewModel jointViewModel)
    {
        if (jointPropertyChangedHandlers.ContainsKey(jointViewModel)) return;

        PropertyChangedEventHandler handler = (_, pce) =>
        {
            if (IsReplacingState) return;

            switch (pce.PropertyName)
            {
                case nameof(jointViewModel.WasPossiblyDragged) when jointViewModel.WasPossiblyDragged:
                    jointViewModel.WasPossiblyDragged = false;
                    EvaluateDirtiness();
                    break;
                case nameof(jointViewModel.Name) or nameof(jointViewModel.Type):
                    EvaluateDirtiness();
                    if (jointViewModel.Type == JointType.HeadTube)
                    {
                        RecalculateHeadAngle();
                    }
                    break;
                case nameof(jointViewModel.X):
                case nameof(jointViewModel.Y):
                    if (jointViewModel.Type is JointType.BottomBracket or JointType.RearWheel)
                    {
                        UpdatePixelsToMillimeters();
                    }
                    if (jointViewModel.Type is JointType.FrontWheel or JointType.RearWheel)
                    {
                        NotifyWheelJointPropertiesChanged();
                    }
                    NotifyCanvasBoundsChanged();
                    RecalculateHeadAngle();
                    break;
            }
        };

        jointPropertyChangedHandlers[jointViewModel] = handler;
        jointViewModel.PropertyChanged += handler;
    }

    private void DetachJointPropertyChangedHandler(JointViewModel jointViewModel)
    {
        if (!jointPropertyChangedHandlers.Remove(jointViewModel, out var handler)) return;

        jointViewModel.PropertyChanged -= handler;
    }

    private void DetachAllJointPropertyChangedHandlers()
    {
        foreach (var pair in jointPropertyChangedHandlers)
        {
            pair.Key.PropertyChanged -= pair.Value;
        }

        jointPropertyChangedHandlers.Clear();
    }

    private void AttachLinkPropertyChangedHandler(LinkViewModel linkViewModel)
    {
        if (linkPropertyChangedHandlers.ContainsKey(linkViewModel)) return;

        PropertyChangedEventHandler handler = (_, pce) =>
        {
            if (IsReplacingState) return;

            if (pce.PropertyName is nameof(linkViewModel.A) or nameof(linkViewModel.B))
            {
                EvaluateDirtiness();
            }
        };

        linkPropertyChangedHandlers[linkViewModel] = handler;
        linkViewModel.PropertyChanged += handler;
    }

    private void DetachLinkPropertyChangedHandler(LinkViewModel linkViewModel)
    {
        if (!linkPropertyChangedHandlers.Remove(linkViewModel, out var handler)) return;

        linkViewModel.PropertyChanged -= handler;
    }

    private void DetachAllLinkPropertyChangedHandlers()
    {
        foreach (var pair in linkPropertyChangedHandlers)
        {
            pair.Key.PropertyChanged -= pair.Value;
        }

        linkPropertyChangedHandlers.Clear();
    }

    private bool DidJointsChanged()
    {
        if (acceptedSnapshot.Linkage is null || PixelsToMillimeters is null || Image is null) return false;

        var joints2 = JointViewModels.Select(jvm => jvm.ToJoint(Image.Size.Height, PixelsToMillimeters.Value)).ToList();
        return acceptedSnapshot.Linkage.Joints.Count != joints2.Count || !acceptedSnapshot.Linkage.Joints.All(j => joints2.Contains(j));
    }

    private bool DidLinksChanged()
    {
        if (acceptedSnapshot.Linkage is null || PixelsToMillimeters is null || Image is null) return false;

        var links2 = LinkViewModels
            .Where(lvm => lvm != shockViewModel && lvm.A is not null && lvm.B is not null)
            .Select(lvm => lvm.ToLink(Image.Size.Height, PixelsToMillimeters.Value)).ToList();
        return acceptedSnapshot.Linkage.Links.Count != links2.Count || !acceptedSnapshot.Linkage.Links.All(l => links2.Contains(l));
    }

    private void SetupJointsListeners()
    {
        JointViewModels.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                DetachAllJointPropertyChangedHandlers();
            }
            else
            {
                if (e.OldItems is not null)
                {
                    foreach (var item in e.OldItems)
                    {
                        if (item is JointViewModel jointViewModel)
                        {
                            DetachJointPropertyChangedHandler(jointViewModel);
                        }
                    }
                }

                if (e.NewItems is not null)
                {
                    foreach (var item in e.NewItems)
                    {
                        if (item is JointViewModel jointViewModel)
                        {
                            AttachJointPropertyChangedHandler(jointViewModel);
                        }
                    }
                }
            }

            if (IsReplacingState) return;

            EvaluateDirtiness();
            NotifyCanvasBoundsChanged();
            NotifyWheelJointPropertiesChanged();
            RecalculateHeadAngle();
        };
    }

    private void SetupLinksListeners()
    {
        LinkViewModels.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                DetachAllLinkPropertyChangedHandlers();
            }
            else
            {
                if (e.OldItems is not null)
                {
                    foreach (var item in e.OldItems)
                    {
                        if (item is LinkViewModel linkViewModel)
                        {
                            DetachLinkPropertyChangedHandler(linkViewModel);
                        }
                    }
                }

                if (e.NewItems is not null)
                {
                    foreach (var item in e.NewItems)
                    {
                        if (item is LinkViewModel linkViewModel)
                        {
                            AttachLinkPropertyChangedHandler(linkViewModel);
                        }
                    }
                }
            }

            if (IsReplacingState) return;

            EvaluateDirtiness();
        };
    }

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
            DidJointsChanged() ||
            DidLinksChanged() ||
            FrontWheelRimSize != acceptedSnapshot.FrontWheelRimSize ||
            !MathUtils.AreEqual(FrontWheelTireWidth, acceptedSnapshot.FrontWheelTireWidth) ||
            !MathUtils.AreEqual(FrontWheelDiameter, acceptedSnapshot.FrontWheelDiameterMm) ||
            RearWheelRimSize != acceptedSnapshot.RearWheelRimSize ||
            !MathUtils.AreEqual(RearWheelTireWidth, acceptedSnapshot.RearWheelTireWidth) ||
            !MathUtils.AreEqual(RearWheelDiameter, acceptedSnapshot.RearWheelDiameterMm) ||
            !MathUtils.AreEqual(ImageRotationDegrees, acceptedSnapshot.ImageRotationDegrees) ||
            !ReferenceEquals(Image, acceptedSnapshot.Image);
    }

    protected override bool CanSave()
    {
        EvaluateDirtiness();

        var frontHasDiameter = FrontWheelDiameter.HasValue;
        var rearHasDiameter = RearWheelDiameter.HasValue;
        var wheelsValid = frontHasDiameter == rearHasDiameter;

        return IsDirty &&
               HeadAngle is not null &&
               ForksStroke is not null &&
               (ShockStroke is null || (Image is not null && Chainstay is not null)) &&
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
    private void DoubleTapped(TappedEventArgs args)
    {
        var position = args.GetPosition(args.Source as Visual);
        JointViewModels.Add(new JointViewModel($"Point{pointNumber++}", JointType.Floating, position.X, position.Y, true));
    }

    [RelayCommand]
    private void Tapped()
    {
        SelectedLink = null;
        SelectedPoint = null;
        ClearSelections();
    }

    [RelayCommand]
    private void PointTapped(TappedEventArgs args)
    {
        if (args.Source is not Visual npv) return;

        ClearSelections();
        SelectedLink = null;
        SelectedPoint = npv.DataContext as JointViewModel;
        SelectedPoint!.IsSelected = true;

        // Prevent Tapped event on the Canvas, which would deselect the point.
        args.Handled = true;
    }

    [RelayCommand]
    private void LinkTapped(TappedEventArgs args)
    {
        if (args.Source is not Line lv) return;

        ClearSelections();
        SelectedPoint = null;

        SelectedLink = lv.DataContext as LinkViewModel;
        SelectedLink!.IsSelected = true;

        // Prevent Tapped event on the Canvas, which would deselect the link.
        args.Handled = true;
    }

    [RelayCommand]
    private void DeleteSelectedItem()
    {
        if (SelectedLink is not null && !SelectedLink.IsImmutable)
        {
            LinkViewModels.Remove(SelectedLink);
        }
        else if (SelectedPoint is not null && SelectedPoint.Immutability == Immutability.Modifiable)
        {
            var linksToDelete = LinkViewModels.Where(l => l.A == SelectedPoint || l.B == SelectedPoint).ToList();
            foreach (var link in linksToDelete)
            {
                LinkViewModels.Remove(link);
            }
            JointViewModels.Remove(SelectedPoint);
            ClearSelections();
        }
    }

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
                    Image = loaded.Bitmap;
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
    private void CreateLink()
    {
        var link = new LinkViewModel(null, null);
        link.UpdateLength(PixelsToMillimeters);
        LinkViewModels.Add(link);
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
