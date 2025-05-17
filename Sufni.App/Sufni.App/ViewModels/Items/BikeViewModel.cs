using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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

    [ObservableProperty] private double? pixelsToMillimeters;

    public ObservableCollection<JointViewModel> JointViewModels { get; } = [];
    public ObservableCollection<LinkViewModel> LinkViewModels { get; } = [];
    [ObservableProperty] private JointViewModel? selectedPoint;
    [ObservableProperty] private LinkViewModel? selectedLink;
    [ObservableProperty] private bool overlayVisible;

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

    public BikeViewModel()
    {
        bike = new Bike();

        SetupJointsListeners();
        SetupLinksListeners();
    }

    public BikeViewModel(Bike bike, bool fromDatabase)
    {
        this.bike = bike;
        IsInDatabase = fromDatabase;
        ResetImplementation();
        SetupJointsListeners();
        SetupLinksListeners();
        
        UpdateFromBike();
        
        // If this is a BikeViewModel created from scratch, we need to add the mandatory joints.
        if (!fromDatabase) AddInitialJoints();
    }

    protected override void EvaluateDirtiness()
    {
        IsDirty =
            !IsInDatabase ||
            Name != bike.Name ||
            !AreEqual(HeadAngle, bike.HeadAngle) ||
            !AreEqual(ForksStroke, bike.ForkStroke) ||
            !AreEqual(ShockStroke, bike.ShockStroke) ||
            !AreEqual(Chainstay, bike.Chainstay) ||
            DidJointsChanged() ||
            DidLinksChanged();
    }

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
        UpdateFromBike();
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

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Export()
    {
        if (!CheckLinkage())
        {
            ErrorMessages.Add("Linkage movement could not be calculated. Please check the joints and links!");
            return;
        }

        var filesService = App.Current?.Services?.GetService<IFilesService>();
        Debug.Assert(filesService != null);

        var file = await filesService.SaveBikeFileAsync();
        if (file is null) return;
        
        var bikeToExport = ToBike();
        var bikeJson = bikeToExport.ToJson();
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync(bikeJson);
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

    private Bike ToBike()
    {
        Debug.Assert(HeadAngle is not null);
        Debug.Assert(ForksStroke is not null);

        var newBike = new Bike(Id, Name ?? $"bike {Id}")
        {
            HeadAngle = HeadAngle.Value,
            ForkStroke = ForksStroke
        };

        // If we don't have a rear suspension, we can return here
        if (ShockStroke is null) return newBike;
        
        Debug.Assert(PixelsToMillimeters is not null);
        newBike.ShockStroke = ShockStroke;
        newBike.Image = Image;
        newBike.PixelsToMillimeters = PixelsToMillimeters.Value;
        newBike.Linkage = CreateLinkage();

        return newBike;
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
    }


    private bool CanExport()
    {
        return HeadAngle is not null &&
               ForksStroke is not null &&
               (ShockStroke is null || (Image is not null && Chainstay is not null));
    }

    private static bool AreEqual(double? a, double? b, double epsilon = 1e-3)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return Math.Abs(a.Value - b.Value) < epsilon;
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

        var links2 = LinkViewModels.Where(lvm => lvm != shockViewModel).Select(lvm => lvm.ToLink(Image.Size.Height, PixelsToMillimeters.Value)).ToList();
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
}
