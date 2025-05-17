using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
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
    private Bike bike;
    public bool IsInDatabase;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private double headAngle;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private double? forksStroke;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private double? shockStroke;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private Bitmap? image;
    [ObservableProperty] private double? chainstay;
    [ObservableProperty] private double? pixelsToMillimeters;

    public ObservableCollection<JointViewModel> JointViewModels { get; } = [];
    public ObservableCollection<LinkViewModel> LinkViewModels { get; } = [];
    [ObservableProperty] private JointViewModel? selectedPoint;
    [ObservableProperty] private LinkViewModel? selectedLink;


    [ObservableProperty] private bool overlayVisible;
    [ObservableProperty] private static Color wheelColor = Colors.Cyan;
    [ObservableProperty] private Brush wheelBrush = new SolidColorBrush(wheelColor);
    [ObservableProperty] private static Color groundColor = Colors.OrangeRed;
    [ObservableProperty] private static Color linkColor = Colors.HotPink;
    [ObservableProperty] private static Color bottomBracketColor = Colors.Purple;

    private uint pointNumber = 1;
    private LinkViewModel? shockViewModel;

    // Handle link selection coming from the table.
    partial void OnSelectedLinkChanged(LinkViewModel? value)
    {
        if (SelectedLink is not null)
        {
            ClearSelections();
            SelectedLink.IsSelected = true;
            SelectedPoint = null;
        }
    }
    // Handle point selection coming from the table.
    partial void OnSelectedPointChanged(JointViewModel? value)
    {
        if (SelectedPoint is not null)
        {
            ClearSelections();
            SelectedPoint.IsSelected = true;
            SelectedLink = null;
        }
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
    }

    partial void OnWheelColorChanged(Color value)
    {
        PointTypeToBrushMapping[JointType.RearWheel] = new SolidColorBrush(value);
        PointTypeToBrushMapping[JointType.FrontWheel] = new SolidColorBrush(value);
        WheelBrush = new SolidColorBrush(value);
    }

    partial void OnGroundColorChanged(Color value)
    {
        PointTypeToBrushMapping[JointType.Fixed] = new SolidColorBrush(value);
    }

    partial void OnLinkColorChanged(Color value)
    {
        PointTypeToBrushMapping[JointType.Floating] = new SolidColorBrush(value);
    }

    partial void OnBottomBracketColorChanged(Color value)
    {
        PointTypeToBrushMapping[JointType.BottomBracket] = new SolidColorBrush(value);
    }

    public BikeViewModel()
    {
        bike = new Bike();
        IsInDatabase = false;
        ResetImplementation();
    }

    public BikeViewModel(Bike bike, bool fromDatabase)
    {
        this.bike = bike;
        IsInDatabase = fromDatabase;
        ResetImplementation();
    }

    public static Dictionary<JointType, Brush> PointTypeToBrushMapping { get; } = new()
    {
        { JointType.RearWheel, new SolidColorBrush(wheelColor)},
        { JointType.FrontWheel, new SolidColorBrush(wheelColor)},
        { JointType.Fixed, new SolidColorBrush(groundColor)},
        { JointType.Floating, new SolidColorBrush(linkColor)},
        { JointType.BottomBracket, new SolidColorBrush(bottomBracketColor)},
    };

    protected override void EvaluateDirtiness()
    {
        IsDirty =
            !IsInDatabase ||
            Name != bike.Name ||
            ForksStroke != bike.ForkStroke ||
            ShockStroke != bike.ShockStroke ||
            Chainstay != bike.Chainstay; //TODO: account for linkage changes too
    }

    protected override async Task SaveImplementation()
    {
        var databaseService = App.Current?.Services?.GetService<IDatabaseService>();
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        Debug.Assert(shockViewModel is not null);
        Debug.Assert(Image is not null);
        Debug.Assert(PixelsToMillimeters is not null, "PxelsToMillimeters is not null");
        Debug.Assert(ShockStroke is not null, "MaxShockStroke is not null");

        try
        {
            var linkage = new Linkage
            {
                Joints = [.. JointViewModels.Select(p => p.ToJoint(Image.Size.Height, PixelsToMillimeters.Value))],
                Links = [.. LinkViewModels.Where(l => l != shockViewModel).Select(l => l.ToLink(Image.Size.Height, PixelsToMillimeters.Value))],
                Shock = shockViewModel.ToLink(Image.Size.Height, PixelsToMillimeters.Value),
                ShockStroke = ShockStroke.Value
            };

            var newBike = new Bike(Id, Name ?? $"bike {Id}")
            {
                Linkage = linkage,
                HeadAngle = HeadAngle,
                ForkStroke = ForksStroke, // ShockStroke is set in linkage
                Image = Image,
                PixelsToMillimeters = PixelsToMillimeters.Value,
            };
            Id = await databaseService.PutBikeAsync(newBike);
            bike = newBike;

            SaveCommand.NotifyCanExecuteChanged();
            ResetCommand.NotifyCanExecuteChanged();

            if (!IsInDatabase)
            {
                var mainPagesViewModel = App.Current?.Services?.GetService<MainPagesViewModel>();
                Debug.Assert(mainPagesViewModel != null, nameof(mainPagesViewModel) + " != null");
                mainPagesViewModel.BikesPage.OnAdded(this);
            }

            IsInDatabase = true;

            OpenPreviousPage();
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Bike could not be saved: {e.Message}");
        }
    }

    protected override Task ResetImplementation()
    {
        Id = bike.Id;
        Name = bike.Name;

        if (bike.Linkage is null || bike.Image is null)
        {
            AddInitialJoints();
            return Task.CompletedTask;
        }

        JointViewModels.Clear();
        var jointViewModels = bike.Linkage.Joints.Select(j => JointViewModel.FromJoint(j, bike.Image.Size.Height, bike.PixelsToMillimeters));
        foreach (var jvm in jointViewModels)
        {
            JointViewModels.Add(jvm);
        }

        LinkViewModels.Clear();
        var linkViewModels = bike.Linkage.Links.Select(l => LinkViewModel.FromLink(l, JointViewModels));
        foreach (var link in linkViewModels)
        {
            LinkViewModels.Add(link);
        }

        Image = bike.Image;
        shockViewModel = LinkViewModel.FromLink(bike.Linkage.Shock, JointViewModels);
        LinkViewModels.Add(shockViewModel);
        Chainstay = bike.Chainstay; // this also updates PixelsToMillimeters
        ForksStroke = bike.ForkStroke;
        ShockStroke = bike.Linkage.ShockStroke;

        return Task.CompletedTask;
    }

    protected override bool CanDelete()
    {
        var mainPagesViewModel = App.Current?.Services?.GetService<MainPagesViewModel>();
        Debug.Assert(mainPagesViewModel != null, nameof(mainPagesViewModel) + " != null");

        return !mainPagesViewModel.SetupsPage.Items.Any(s =>
            s is SetupViewModel svm &&
            svm.SelectedBike != null &&
            svm.SelectedBike.Id == Id);
    }

    [RelayCommand]
    private void DoubleTapped(TappedEventArgs args)
    {
        var position = args.GetPosition(args.Source as Visual);
        JointViewModels.Add(new JointViewModel($"Point{pointNumber++}", JointType.Floating, position.X, position.Y));
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
        if (args.Source is Visual npv)
        {
            ClearSelections();
            SelectedLink = null;

            SelectedPoint = npv.DataContext as JointViewModel;
            SelectedPoint!.IsSelected = true;

            // Prevent Tapped event on the Canvas, which would deselect the point.
            args.Handled = true;
        }
    }

    [RelayCommand]
    private void LinkTapped(TappedEventArgs args)
    {
        if (args.Source is Line lv)
        {
            ClearSelections();
            SelectedPoint = null;

            SelectedLink = lv.DataContext as LinkViewModel;
            SelectedLink!.IsSelected = true;

            // Prevent Tapped event on the Canvas, which would deselect the link.
            args.Handled = true;
        }
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

        Image = new(file.Path.AbsolutePath);
    }

    [RelayCommand]
    private void CreateLink()
    {
        var link = new LinkViewModel(null, null);
        link.UpdateLength(PixelsToMillimeters);
        LinkViewModels.Add(link);
    }

    private void AddInitialJoints()
    {
        JointViewModels.Add(new JointViewModel("Front wheel", JointType.FrontWheel, 100, 150));

        var bottomBracket = new JointViewModel("Bottom bracket", JointType.BottomBracket, 100, 200);
        var rearWheel = new JointViewModel("Rear wheel", JointType.RearWheel, 100, 100);
        bottomBracket.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(JointViewModel.X) or nameof(JointViewModel.Y))
            {
                UpdatePixelsToMillimeters(bottomBracket, rearWheel);
            }
        };
        rearWheel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(JointViewModel.X) or nameof(JointViewModel.Y))
            {
                UpdatePixelsToMillimeters(bottomBracket, rearWheel);
            }
        };
        JointViewModels.Add(bottomBracket);
        JointViewModels.Add(rearWheel);

        var shockEye1 = new JointViewModel("Shock eye 1", JointType.Floating, 100, 250);
        var shockEye2 = new JointViewModel("Shock eye 2", JointType.Floating, 100, 300);
        JointViewModels.Add(shockEye1);
        JointViewModels.Add(shockEye2);
        shockViewModel = new(shockEye1, shockEye2, "Shock");
        LinkViewModels.Add(shockViewModel);
    }

    private void UpdatePixelsToMillimeters(JointViewModel? bottomBracket = null, JointViewModel? rearWheel = null)
    {
        var bb = bottomBracket ?? JointViewModels.First(p => p.Type == JointType.BottomBracket);
        var rw = rearWheel ?? JointViewModels.First(p => p.Type == JointType.RearWheel);
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
}
