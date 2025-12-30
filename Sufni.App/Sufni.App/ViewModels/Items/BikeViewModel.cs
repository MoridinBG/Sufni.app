using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.ViewModels.LinkageParts;
using Sufni.Kinematics;

namespace Sufni.App.ViewModels.Items;

public partial class BikeViewModel : ItemViewModelBase
{
    public bool IsInDatabase;

    #region Private fields

    private Bike bike;
    private uint pointNumber = 1;
    private LinkViewModel? shockViewModel;

    #endregion Private fields

    #region Observable properties

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
    private Bitmap? image;
    
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private double? chainstay;

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

    [ObservableProperty]
    private double imageRotationDegrees;

    [ObservableProperty] private double? pixelsToMillimeters;

    public ObservableCollection<JointViewModel> JointViewModels { get; } = [];
    public ObservableCollection<LinkViewModel> LinkViewModels { get; } = [];
    [ObservableProperty] private JointViewModel? selectedPoint;
    [ObservableProperty] private LinkViewModel? selectedLink;
    [ObservableProperty] private bool overlayVisible;

    [ObservableProperty] private CoordinateList? leverageRatioData;

    #endregion Observable properties

    #region Wheel properties

    public record RimSizeOption(EtrtoRimSize Value, string DisplayName);

    public static RimSizeOption[] RimSizeOptions { get; } = Enum.GetValues<EtrtoRimSize>()
        .Select(r => new RimSizeOption(r, r.DisplayName))
        .ToArray();

    public bool HasWheels =>
        FrontWheelDiameter.HasValue &&
        RearWheelDiameter.HasValue &&
        GetFrontWheelJoint() != null &&
        GetRearWheelJoint() != null;

    public double? FrontWheelRadiusPixels =>
        FrontWheelDiameter / 2.0 / PixelsToMillimeters;

    public double? RearWheelRadiusPixels =>
        RearWheelDiameter / 2.0 / PixelsToMillimeters;

    public double FrontWheelCircleLeft => GetFrontWheelJoint()?.X - (FrontWheelRadiusPixels ?? 0) ?? 0;
    public double FrontWheelCircleTop => GetFrontWheelJoint()?.Y - (FrontWheelRadiusPixels ?? 0) ?? 0;
    public double FrontWheelCircleDiameter => (FrontWheelRadiusPixels ?? 0) * 2;

    public double RearWheelCircleLeft => GetRearWheelJoint()?.X - (RearWheelRadiusPixels ?? 0) ?? 0;
    public double RearWheelCircleTop => GetRearWheelJoint()?.Y - (RearWheelRadiusPixels ?? 0) ?? 0;
    public double RearWheelCircleDiameter => (RearWheelRadiusPixels ?? 0) * 2;

    public string FrontWheelDisplayText => FormatWheelDisplay(FrontWheelRimSize, FrontWheelTireWidth, FrontWheelDiameter);
    public string RearWheelDisplayText => FormatWheelDisplay(RearWheelRimSize, RearWheelTireWidth, RearWheelDiameter);

    private JointViewModel? GetFrontWheelJoint() =>
        JointViewModels.FirstOrDefault(j => j.Type == JointType.FrontWheel);

    private JointViewModel? GetRearWheelJoint() =>
        JointViewModels.FirstOrDefault(j => j.Type == JointType.RearWheel);

    private static string FormatWheelDisplay(EtrtoRimSize? rimSize, double? tireWidth, double? diameter)
    {
        if (!diameter.HasValue) return "";
        if (rimSize.HasValue && tireWidth.HasValue)
            return $"{rimSize.Value.DisplayName} / {tireWidth:0.00}\"";
        return $"{diameter:0.0} mm";
    }

    private void NotifyFrontWheelPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasWheels));
        OnPropertyChanged(nameof(FrontWheelRadiusPixels));
        OnPropertyChanged(nameof(FrontWheelCircleLeft));
        OnPropertyChanged(nameof(FrontWheelCircleTop));
        OnPropertyChanged(nameof(FrontWheelCircleDiameter));
        OnPropertyChanged(nameof(FrontWheelDisplayText));
    }

    private void NotifyRearWheelPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasWheels));
        OnPropertyChanged(nameof(RearWheelRadiusPixels));
        OnPropertyChanged(nameof(RearWheelCircleLeft));
        OnPropertyChanged(nameof(RearWheelCircleTop));
        OnPropertyChanged(nameof(RearWheelCircleDiameter));
        OnPropertyChanged(nameof(RearWheelDisplayText));
    }

    private void NotifyWheelJointPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasWheels));
        OnPropertyChanged(nameof(FrontWheelCircleLeft));
        OnPropertyChanged(nameof(FrontWheelCircleTop));
        OnPropertyChanged(nameof(RearWheelCircleLeft));
        OnPropertyChanged(nameof(RearWheelCircleTop));
    }

    #endregion Wheel properties

    #region Property change handlers

    // Handle link selection coming from the table.
    partial void OnSelectedLinkChanged(LinkViewModel? value)
    {
        if (SelectedLink is null) return;
        ClearSelections();
        SelectedLink.IsSelected = true;
        SelectedPoint = null;
    }
    // Handle point selection coming from the table.
    partial void OnSelectedPointChanged(JointViewModel? value)
    {
        if (SelectedPoint is null) return;
        ClearSelections();
        SelectedPoint.IsSelected = true;
        SelectedLink = null;
    }

    partial void OnChainstayChanged(double? value)
    {
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
        foreach (var link in LinkViewModels)
        {
            link.UpdateLength(PixelsToMillimeters);
        }
        NotifyFrontWheelPropertiesChanged();
        NotifyRearWheelPropertiesChanged();
    }

    partial void OnFrontWheelRimSizeChanged(EtrtoRimSize? value)
    {
        RecalculateFrontWheelDiameter();
        EvaluateDirtiness();
    }

    partial void OnFrontWheelTireWidthChanged(double? value)
    {
        RecalculateFrontWheelDiameter();
        EvaluateDirtiness();
    }

    partial void OnFrontWheelDiameterChanged(double? value)
    {
        // User manually edited diameter - clear rim size and tire width
        // Use backing fields to avoid triggering their change handlers
        frontWheelRimSize = null;
        OnPropertyChanged(nameof(FrontWheelRimSize));
        frontWheelTireWidth = null;
        OnPropertyChanged(nameof(FrontWheelTireWidth));

        NotifyFrontWheelPropertiesChanged();
        EvaluateDirtiness();
    }

    partial void OnRearWheelRimSizeChanged(EtrtoRimSize? value)
    {
        RecalculateRearWheelDiameter();
        EvaluateDirtiness();
    }

    partial void OnRearWheelTireWidthChanged(double? value)
    {
        RecalculateRearWheelDiameter();
        EvaluateDirtiness();
    }

    partial void OnRearWheelDiameterChanged(double? value)
    {
        // User manually edited diameter - clear rim size and tire width
        // Use backing fields to avoid triggering their change handlers
        rearWheelRimSize = null;
        OnPropertyChanged(nameof(RearWheelRimSize));
        rearWheelTireWidth = null;
        OnPropertyChanged(nameof(RearWheelTireWidth));

        NotifyRearWheelPropertiesChanged();
        EvaluateDirtiness();
    }

    private void RecalculateFrontWheelDiameter()
    {
        if (FrontWheelRimSize.HasValue && FrontWheelTireWidth.HasValue)
        {
            var beadDiameter = FrontWheelRimSize.Value.BeadDiameterMm;
            var diameter = beadDiameter + (FrontWheelTireWidth.Value * 2 * 25.4);
            // Round to 1 decimal to match NumericUpDown display format.
            // Otherwise the NumericUpDown rounds it,
            // writes the value back and triggers cleanup of rim & width as in manual update
            frontWheelDiameter = Math.Round(diameter, 1);
            OnPropertyChanged(nameof(FrontWheelDiameter));
        }
        NotifyFrontWheelPropertiesChanged();
    }

    private void RecalculateRearWheelDiameter()
    {
        if (RearWheelRimSize.HasValue && RearWheelTireWidth.HasValue)
        {
            var beadDiameter = RearWheelRimSize.Value.BeadDiameterMm;
            var diameter = beadDiameter + (RearWheelTireWidth.Value * 2 * 25.4);
            rearWheelDiameter = Math.Round(diameter, 1);
            OnPropertyChanged(nameof(RearWheelDiameter));
        }
        NotifyRearWheelPropertiesChanged();
    }

    #endregion Property change handlers

    #region Constructors

    public BikeViewModel()
    {
        IsInDatabase = false;
        bike = new Bike();

        SetupJointsListeners();
        SetupLinksListeners();
    }

    public BikeViewModel(Bike bike, bool fromDatabase)
    {
        IsInDatabase = fromDatabase;
        this.bike = bike;
        
        SetupJointsListeners();
        SetupLinksListeners();
        
        UpdateFromBike();
        
        // If this is a BikeViewModel created from scratch, we need to add the mandatory joints.
        if (!fromDatabase) AddInitialJoints();
    }

    #endregion Constructors

    #region Private methods

    private Bike ToBike()
    {
        Debug.Assert(HeadAngle is not null);
        Debug.Assert(ForksStroke is not null);

        var newBike = new Bike(Id, Name ?? $"bike {Id}")
        {
            HeadAngle = HeadAngle.Value,
            ForkStroke = ForksStroke,
            Chainstay = Chainstay
        };

        // If we don't have a rear suspension, we can return here
        if (ShockStroke is null) return newBike;

        Debug.Assert(PixelsToMillimeters is not null);
        newBike.ShockStroke = ShockStroke;
        newBike.Image = Image;
        newBike.PixelsToMillimeters = PixelsToMillimeters.Value;
        newBike.Linkage = CreateLinkage();

        // Wheel properties
        newBike.FrontWheelRimSize = FrontWheelRimSize;
        newBike.FrontWheelTireWidth = FrontWheelTireWidth;
        newBike.FrontWheelDiameterMm = FrontWheelDiameter;
        newBike.RearWheelRimSize = RearWheelRimSize;
        newBike.RearWheelTireWidth = RearWheelTireWidth;
        newBike.RearWheelDiameterMm = RearWheelDiameter;
        newBike.ImageRotationDegrees = ImageRotationDegrees;

        return newBike;
    }

    private void AddInitialJoints()
    {
        var mapping = new JointNameMapping();
        JointViewModels.Add(new JointViewModel(mapping.FrontWheel, JointType.FrontWheel, 100, 150));
        JointViewModels.Add(new JointViewModel(mapping.BottomBracket, JointType.BottomBracket, 100, 200)); 
        JointViewModels.Add(new JointViewModel(mapping.RearWheel, JointType.RearWheel, 100, 100));

        var shockEye1 = new JointViewModel(mapping.ShockEye1, JointType.Floating, 100, 250);
        var shockEye2 = new JointViewModel(mapping.ShockEye2, JointType.Floating, 100, 300);
        JointViewModels.Add(shockEye1);
        JointViewModels.Add(shockEye2);
        shockViewModel = new LinkViewModel(shockEye1, shockEye2, "Shock");
        LinkViewModels.Add(shockViewModel);
    }

    private void UpdatePixelsToMillimeters()
    {
        var bb = JointViewModels.FirstOrDefault(p => p.Type == JointType.BottomBracket);
        var rw = JointViewModels.FirstOrDefault(p => p.Type == JointType.RearWheel);
        if (bb is null || rw is null) return;

        var distance = Math.Sqrt(Math.Pow(rw.X - bb.X, 2) + Math.Pow(rw.Y - bb.Y, 2));
        PixelsToMillimeters = Chainstay / distance;
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

    private void UpdateFromBike()
    {
        JointViewModels.Clear();
        LinkViewModels.Clear();

        if (bike.Linkage is not null)
        {
            Debug.Assert(bike.Image is not null);

            var jointViewModels = bike.Linkage.Joints.Select(j => JointViewModel.FromJoint(j, bike.Image.Size.Height, bike.PixelsToMillimeters));
            foreach (var jvm in jointViewModels)
            {
                JointViewModels.Add(jvm);
            }

            var linkViewModels = bike.Linkage.Links.Select(l => LinkViewModel.FromLink(l, JointViewModels));
            foreach (var link in linkViewModels)
            {
                LinkViewModels.Add(link);
            }
            shockViewModel = LinkViewModel.FromLink(bike.Linkage.Shock, JointViewModels);
            LinkViewModels.Add(shockViewModel);
            ShockStroke = bike.Linkage.ShockStroke;
            Chainstay = bike.Chainstay; // this also updates PixelsToMillimeters
            Image = bike.Image;
        }

        Id = bike.Id;
        Name = bike.Name;
        HeadAngle = bike.HeadAngle;
        ForksStroke = bike.ForkStroke;

        // Wheel properties - use backing fields to avoid triggering change handlers
        frontWheelRimSize = bike.FrontWheelRimSize;
        frontWheelTireWidth = bike.FrontWheelTireWidth;
        frontWheelDiameter = bike.FrontWheelDiameterMm;
        rearWheelRimSize = bike.RearWheelRimSize;
        rearWheelTireWidth = bike.RearWheelTireWidth;
        rearWheelDiameter = bike.RearWheelDiameterMm;
        imageRotationDegrees = bike.ImageRotationDegrees;

        OnPropertyChanged(nameof(FrontWheelRimSize));
        OnPropertyChanged(nameof(FrontWheelTireWidth));
        OnPropertyChanged(nameof(FrontWheelDiameter));
        OnPropertyChanged(nameof(RearWheelRimSize));
        OnPropertyChanged(nameof(RearWheelTireWidth));
        OnPropertyChanged(nameof(RearWheelDiameter));
        OnPropertyChanged(nameof(ImageRotationDegrees));

        NotifyFrontWheelPropertiesChanged();
        NotifyRearWheelPropertiesChanged();

        CheckLinkage(bike.Linkage!);
    }

    private bool CheckLinkage(Linkage linkage)
    {
        try
        {
            var solver = new KinematicSolver(linkage);
            var solution = solver.SolveSuspensionMotion();
            var characteristics = new BikeCharacteristics(solution);
            LeverageRatioData = characteristics.LeverageRatioData;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool DidJointsChanged()
    {
        if (bike.Linkage is null || PixelsToMillimeters is null || Image is null) return false;
        
        var joints2 = JointViewModels.Select(jvm => jvm.ToJoint(Image.Size.Height, PixelsToMillimeters.Value)).ToList();
        return bike.Linkage.Joints.Count != joints2.Count || !bike.Linkage.Joints.All(j => joints2.Contains(j));
    }
    
    private bool DidLinksChanged()
    {
        if (bike.Linkage is null || PixelsToMillimeters is null || Image is null) return false;

        var links2 = LinkViewModels
            .Where(lvm => lvm != shockViewModel && lvm.A is not null && lvm.B is not null)
            .Select(lvm => lvm.ToLink(Image.Size.Height, PixelsToMillimeters.Value)).ToList();
        return bike.Linkage.Links.Count != links2.Count || !bike.Linkage.Links.All(l => links2.Contains(l));
    }

    private void SetupJointsListeners()
    {
        JointViewModels.CollectionChanged += (_, e) =>
        {
            EvaluateDirtiness();

            if (e.Action != NotifyCollectionChangedAction.Add) return;
            Debug.Assert(e.NewItems is not null);
            foreach (var item in e.NewItems)
            {
                var jvm = item as JointViewModel;
                Debug.Assert(jvm is not null);

                jvm.PropertyChanged += (_, pce) =>
                {
                    switch (pce.PropertyName)
                    {
                        case nameof(jvm.WasPossiblyDragged) when jvm.WasPossiblyDragged:
                            jvm.WasPossiblyDragged = false;
                            EvaluateDirtiness();
                            break;
                        case nameof(jvm.Name) or nameof(jvm.Type):
                            EvaluateDirtiness();
                            break;
                        case nameof(jvm.X) when jvm.Type is JointType.BottomBracket or JointType.RearWheel:
                        case nameof(jvm.Y) when jvm.Type is JointType.BottomBracket or JointType.RearWheel:
                            UpdatePixelsToMillimeters();
                            break;
                        case nameof(jvm.X) when jvm.Type is JointType.FrontWheel or JointType.RearWheel:
                        case nameof(jvm.Y) when jvm.Type is JointType.FrontWheel or JointType.RearWheel:
                            NotifyWheelJointPropertiesChanged();
                            break;
                    }
                };
            }
        };
    }
    
    private void SetupLinksListeners()
    {
        LinkViewModels.CollectionChanged += (_, e) =>
        {
            EvaluateDirtiness();

            if (e.Action != NotifyCollectionChangedAction.Add) return;
            Debug.Assert(e.NewItems is not null);
            foreach (var item in e.NewItems)
            {
                var lvm = item as LinkViewModel;
                Debug.Assert(lvm is not null);

                lvm.PropertyChanged += (_, pce) =>
                {
                    if (pce.PropertyName is
                        nameof(lvm.A) or 
                        nameof(lvm.B))
                    {
                        EvaluateDirtiness();
                    }
                };
            }
        };
    }

    #endregion Private methods
    
    #region TabPageViewModelBase overrides

    protected override void EvaluateDirtiness()
    {
        IsDirty =
            !IsInDatabase ||
            Name != bike.Name ||
            !MathUtils.AreEqual(HeadAngle, bike.HeadAngle) ||
            !MathUtils.AreEqual(ForksStroke, bike.ForkStroke) ||
            !MathUtils.AreEqual(ShockStroke, bike.ShockStroke) ||
            !MathUtils.AreEqual(Chainstay, bike.Chainstay) ||
            DidJointsChanged() ||
            DidLinksChanged() ||
            FrontWheelRimSize != bike.FrontWheelRimSize ||
            !MathUtils.AreEqual(FrontWheelTireWidth, bike.FrontWheelTireWidth) ||
            !MathUtils.AreEqual(FrontWheelDiameter, bike.FrontWheelDiameterMm) ||
            RearWheelRimSize != bike.RearWheelRimSize ||
            !MathUtils.AreEqual(RearWheelTireWidth, bike.RearWheelTireWidth) ||
            !MathUtils.AreEqual(RearWheelDiameter, bike.RearWheelDiameterMm);
    }

    protected override bool CanSave()
    {
        EvaluateDirtiness();

        // Both wheels must have valid diameter, or neither
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
        var databaseService = App.Current?.Services?.GetService<IDatabaseService>();
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        try
        {
            // Apply rotation if wheels are configured and joints exist
            if (HasWheels && Image is not null && PixelsToMillimeters.HasValue)
            {
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

                if (Math.Abs(deltaRotation) > 0.01)
                {
                    var centerX = Image.Size.Width / 2.0;
                    var centerY = Image.Size.Height / 2.0;

                    foreach (var joint in JointViewModels)
                    {
                        var (newX, newY) = CoordinateRotation.RotatePoint(
                            joint.X, joint.Y, centerX, centerY, deltaRotation);
                        joint.X = newX;
                        joint.Y = newY;
                    }

                    ImageRotationDegrees = newRotation;
                    NotifyWheelJointPropertiesChanged();
                }
            }

            var newBike = ToBike();
            if (!CheckLinkage(newBike.Linkage!))
            {
                ErrorMessages.Add("Linkage movement could not be calculated. Please check the joints and links!");
                return;
            }

            Id = await databaseService.PutAsync(newBike);
            bike = newBike;

            SaveCommand.NotifyCanExecuteChanged();
            ResetCommand.NotifyCanExecuteChanged();
            ExportCommand.NotifyCanExecuteChanged();

            if (!IsInDatabase)
            {
                var mainPagesViewModel = App.Current?.Services?.GetService<MainPagesViewModel>();
                Debug.Assert(mainPagesViewModel != null, nameof(mainPagesViewModel) + " != null");
                mainPagesViewModel.BikesPage.OnAdded(this);
            }

            IsInDatabase = true;

            Debug.Assert(App.Current is not null);
            if (!App.Current.IsDesktop)
            {
                OpenPreviousPage();
            }
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Bike could not be saved: {e.Message}");
        }
    }

    protected override Task ResetImplementation()
    {
        UpdateFromBike();
        return Task.CompletedTask;
    }

    protected override async Task ExportImplementation()
    {
        var filesService = App.Current?.Services?.GetService<IFilesService>();
        Debug.Assert(filesService != null);

        var file = await filesService.SaveBikeFileAsync();
        if (file is null) return;
        
        var bikeJson = bike.ToJson();
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync(bikeJson);
    }

    #endregion TabPageViewModelBase overrides

    #region ItemViewModelBase overrides

    protected override bool CanDelete()
    {
        var mainPagesViewModel = App.Current?.Services?.GetService<MainPagesViewModel>();
        Debug.Assert(mainPagesViewModel != null, nameof(mainPagesViewModel) + " != null");

        return !mainPagesViewModel.SetupsPage.Items.Any(s =>
            s is SetupViewModel { SelectedBike: not null } svm &&
            svm.SelectedBike.Id == Id);
    }

    #endregion ItemViewModelBase overrides

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
    private async Task OpenImage(CancellationToken token)
    {
        var filesService = App.Current?.Services?.GetService<IFilesService>();
        Debug.Assert(filesService != null, nameof(filesService) + " != null");

        var file = await filesService.OpenBikeImageFileAsync();
        if (file is null) return;

        Image = new Bitmap(file.Path.LocalPath);
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
        var filesService = App.Current?.Services?.GetService<IFilesService>();
        Debug.Assert(filesService != null);

        var file = await filesService.OpenBikeFileAsync();
        if (file is null) return;

        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        var bikeFromJson = Bike.FromJson(json);
        if (bikeFromJson is null)
        {
            ErrorMessages.Add("JSON file was not a valid bike file.");
            return;
        }
        
        bike = bikeFromJson;
        UpdateFromBike();
    }

    [RelayCommand]
    private void Loaded()
    {
        CheckLinkage(bike.Linkage!);
    }

    #endregion Commands
}
