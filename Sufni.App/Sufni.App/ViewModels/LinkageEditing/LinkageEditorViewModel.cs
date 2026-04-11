using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ViewModels.LinkageParts;
using Sufni.Kinematics;

namespace Sufni.App.ViewModels.LinkageEditing;

public partial class LinkageEditorViewModel : ObservableObject
{
    private uint pointNumber = 1;
    private LinkViewModel? shockViewModel;
    private readonly ObservableCollection<JointViewModel> jointViewModels = [];
    private readonly ObservableCollection<LinkViewModel> linkViewModels = [];
    private readonly Dictionary<JointViewModel, PropertyChangedEventHandler> jointPropertyChangedHandlers = [];
    private readonly Dictionary<LinkViewModel, PropertyChangedEventHandler> linkPropertyChangedHandlers = [];
    private bool suppressChangeNotifications;

    public event EventHandler<LinkageEditorChange>? Changed;

    public ReadOnlyObservableCollection<JointViewModel> JointViewModels { get; }
    public ReadOnlyObservableCollection<LinkViewModel> LinkViewModels { get; }

    [ObservableProperty] private JointViewModel? selectedPoint;
    [ObservableProperty] private LinkViewModel? selectedLink;
    [ObservableProperty] private double linkStrokeThickness = 10.0;
    [ObservableProperty] private double jointFontSize = 24.0;

    public LinkageEditorViewModel()
    {
        JointViewModels = new ReadOnlyObservableCollection<JointViewModel>(jointViewModels);
        LinkViewModels = new ReadOnlyObservableCollection<LinkViewModel>(linkViewModels);
        SetupJointsListeners();
        SetupLinksListeners();
    }

    partial void OnSelectedLinkChanged(LinkViewModel? value)
    {
        if (value is null || suppressChangeNotifications) return;

        ClearSelections();
        value.IsSelected = true;
        SelectedPoint = null;
        RaiseChanged(new LinkageEditorChange(LinkageEditorChangeKind.SelectionChanged, link: value));
    }

    partial void OnSelectedPointChanged(JointViewModel? value)
    {
        if (value is null || suppressChangeNotifications) return;

        ClearSelections();
        value.IsSelected = true;
        SelectedLink = null;
        RaiseChanged(new LinkageEditorChange(LinkageEditorChangeKind.SelectionChanged, joint: value));
    }

    public void Load(Linkage? linkage, double? imageHeight, double? pixelsToMillimeters)
    {
        RunWithoutNotifications(() =>
        {
            SelectedLink = null;
            SelectedPoint = null;

            jointViewModels.Clear();
            linkViewModels.Clear();
            shockViewModel = null;

            if (linkage is null || imageHeight is null || pixelsToMillimeters is null)
            {
                return;
            }

            var loadedJointViewModels = linkage.Joints.Select(joint =>
                JointViewModel.FromJoint(joint, imageHeight.Value, pixelsToMillimeters.Value));
            foreach (var jointViewModel in loadedJointViewModels)
            {
                this.jointViewModels.Add(jointViewModel);
            }

            var loadedLinkViewModels = linkage.Links.Select(link => LinkViewModel.FromLink(link, JointViewModels));
            foreach (var linkViewModel in loadedLinkViewModels)
            {
                this.linkViewModels.Add(linkViewModel);
            }

            shockViewModel = LinkViewModel.FromLink(linkage.Shock, JointViewModels);
            this.linkViewModels.Add(shockViewModel);
        });

        SetPixelsToMillimeters(pixelsToMillimeters);
    }

    public void AddInitialJoints()
    {
        var mapping = new JointNameMapping();

        JointViewModel EnsureJoint(string name, JointType type, double x, double y)
        {
            var existing = JointViewModels.FirstOrDefault(joint => joint.Name == name);
            if (existing is not null) return existing;

            var jointViewModel = new JointViewModel(name, type, x, y);
            jointViewModels.Add(jointViewModel);
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
            linkViewModels.Add(shockViewModel);
        }
    }

    public void SetPixelsToMillimeters(double? pixelsToMillimeters)
    {
        foreach (var link in LinkViewModels)
        {
            link.UpdateLength(pixelsToMillimeters);
        }
    }

    public void UpdateZoomPresentation(double zoomX)
    {
        LinkStrokeThickness = 45.0 / zoomX;
        var taper = 0.85 + 0.15 * (1.0 - 1.0 / zoomX);
        JointFontSize = 150.0 * taper / zoomX;
    }

    public JointViewModel? GetFrontWheelJoint() =>
        JointViewModels.FirstOrDefault(joint => joint.Type == JointType.FrontWheel);

    public JointViewModel? GetRearWheelJoint() =>
        JointViewModels.FirstOrDefault(joint => joint.Type == JointType.RearWheel);

    public Point? GetFrontWheelCenter()
    {
        var frontWheel = GetFrontWheelJoint();
        return frontWheel is null ? null : new Point(frontWheel.X, frontWheel.Y);
    }

    public Point? GetRearWheelCenter()
    {
        var rearWheel = GetRearWheelJoint();
        return rearWheel is null ? null : new Point(rearWheel.X, rearWheel.Y);
    }

    public Rect? GetJointBounds()
    {
        if (JointViewModels.Count == 0)
        {
            return null;
        }

        const double radius = 20.0;
        var minX = JointViewModels.Min(joint => joint.X) - radius;
        var minY = JointViewModels.Min(joint => joint.Y) - radius;
        var maxX = JointViewModels.Max(joint => joint.X) + radius;
        var maxY = JointViewModels.Max(joint => joint.Y) + radius;
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    public void RotateAll(double deltaRotationDegrees)
    {
        RunWithoutNotifications(() =>
        {
            CoordinateRotation.RotatePoints(jointViewModels, 0, 0, deltaRotationDegrees);

            var items = jointViewModels.ToList();
            jointViewModels.Clear();
            foreach (var item in items)
            {
                jointViewModels.Add(item);
            }
        });
    }

    public Linkage? BuildCurrentLinkage(double? imageHeight, double? pixelsToMillimeters, double? shockStroke)
    {
        if (shockStroke is null || imageHeight is null || pixelsToMillimeters is null || shockViewModel is null)
        {
            return null;
        }

        return Linkage.CreateResolved(
            JointViewModels.Select(joint => joint.ToJoint(imageHeight.Value, pixelsToMillimeters.Value)),
            LinkViewModels.Where(link => link != shockViewModel).Select(link => link.ToLink(imageHeight.Value, pixelsToMillimeters.Value)),
            shockViewModel.ToLink(imageHeight.Value, pixelsToMillimeters.Value),
            shockStroke.Value);
    }

    public bool HasChangesComparedTo(Linkage? baseline, double? imageHeight, double? pixelsToMillimeters)
    {
        if (baseline is null)
        {
            return JointViewModels.Count > 0 || LinkViewModels.Count > 0;
        }

        if (imageHeight is null || pixelsToMillimeters is null)
        {
            return false;
        }

        if (shockViewModel is null)
        {
            return true;
        }

        var joints = JointViewModels.Select(joint => joint.ToJoint(imageHeight.Value, pixelsToMillimeters.Value)).ToList();
        if (baseline.Joints.Count != joints.Count || !baseline.Joints.All(joint => joints.Contains(joint)))
        {
            return true;
        }

        var links = LinkViewModels
            .Where(link => link != shockViewModel && link.A is not null && link.B is not null)
            .Select(link => link.ToLink(imageHeight.Value, pixelsToMillimeters.Value))
            .ToList();

        if (baseline.Links.Count != links.Count || !baseline.Links.All(link => links.Contains(link)))
        {
            return true;
        }

        return baseline.Shock != shockViewModel.ToLink(imageHeight.Value, pixelsToMillimeters.Value);
    }

    private void RunWithoutNotifications(Action action)
    {
        suppressChangeNotifications = true;
        try
        {
            action();
        }
        finally
        {
            suppressChangeNotifications = false;
        }
    }

    private void RaiseChanged(LinkageEditorChange change)
    {
        if (suppressChangeNotifications) return;

        Changed?.Invoke(this, change);
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

    private void AttachJointPropertyChangedHandler(JointViewModel jointViewModel)
    {
        if (jointPropertyChangedHandlers.ContainsKey(jointViewModel)) return;

        PropertyChangedEventHandler handler = (_, eventArgs) =>
        {
            if (suppressChangeNotifications) return;

            switch (eventArgs.PropertyName)
            {
                case nameof(jointViewModel.WasPossiblyDragged) when jointViewModel.WasPossiblyDragged:
                    jointViewModel.WasPossiblyDragged = false;
                    RaiseChanged(new LinkageEditorChange(LinkageEditorChangeKind.DragCompleted, jointViewModel));
                    break;

                case nameof(jointViewModel.Name):
                case nameof(jointViewModel.Type):
                    RaiseChanged(new LinkageEditorChange(LinkageEditorChangeKind.JointMetadataChanged, jointViewModel));
                    break;

                case nameof(jointViewModel.X):
                case nameof(jointViewModel.Y):
                    RaiseChanged(new LinkageEditorChange(LinkageEditorChangeKind.JointCoordinatesChanged, jointViewModel));
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

        PropertyChangedEventHandler handler = (_, eventArgs) =>
        {
            if (suppressChangeNotifications) return;

            if (eventArgs.PropertyName is nameof(linkViewModel.A) or nameof(linkViewModel.B))
            {
                RaiseChanged(new LinkageEditorChange(LinkageEditorChangeKind.LinkEndpointsChanged, link: linkViewModel));
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

    private void SetupJointsListeners()
    {
        jointViewModels.CollectionChanged += (_, eventArgs) =>
        {
            if (eventArgs.Action == NotifyCollectionChangedAction.Reset)
            {
                DetachAllJointPropertyChangedHandlers();
            }
            else
            {
                if (eventArgs.OldItems is not null)
                {
                    foreach (var item in eventArgs.OldItems)
                    {
                        if (item is JointViewModel jointViewModel)
                        {
                            DetachJointPropertyChangedHandler(jointViewModel);
                        }
                    }
                }

                if (eventArgs.NewItems is not null)
                {
                    foreach (var item in eventArgs.NewItems)
                    {
                        if (item is JointViewModel jointViewModel)
                        {
                            AttachJointPropertyChangedHandler(jointViewModel);
                        }
                    }
                }
            }

            if (suppressChangeNotifications) return;

            RaiseChanged(new LinkageEditorChange(LinkageEditorChangeKind.JointStructureChanged));
        };
    }

    private void SetupLinksListeners()
    {
        linkViewModels.CollectionChanged += (_, eventArgs) =>
        {
            if (eventArgs.Action == NotifyCollectionChangedAction.Reset)
            {
                DetachAllLinkPropertyChangedHandlers();
            }
            else
            {
                if (eventArgs.OldItems is not null)
                {
                    foreach (var item in eventArgs.OldItems)
                    {
                        if (item is LinkViewModel linkViewModel)
                        {
                            DetachLinkPropertyChangedHandler(linkViewModel);
                        }
                    }
                }

                if (eventArgs.NewItems is not null)
                {
                    foreach (var item in eventArgs.NewItems)
                    {
                        if (item is LinkViewModel linkViewModel)
                        {
                            AttachLinkPropertyChangedHandler(linkViewModel);
                        }
                    }
                }
            }

            if (suppressChangeNotifications) return;

            RaiseChanged(new LinkageEditorChange(LinkageEditorChangeKind.LinkStructureChanged));
        };
    }

    [RelayCommand]
    private void DoubleTapped(TappedEventArgs args)
    {
        var position = args.GetPosition(args.Source as Visual);
        jointViewModels.Add(new JointViewModel($"Point{pointNumber++}", JointType.Floating, position.X, position.Y, true));
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
        if (args.Source is not Visual visual) return;

        ClearSelections();
        SelectedLink = null;
        SelectedPoint = visual.DataContext as JointViewModel;
        if (SelectedPoint is not null)
        {
            SelectedPoint.IsSelected = true;
        }

        args.Handled = true;
    }

    [RelayCommand]
    private void LinkTapped(TappedEventArgs args)
    {
        if (args.Source is not Line line) return;

        ClearSelections();
        SelectedPoint = null;
        SelectedLink = line.DataContext as LinkViewModel;
        if (SelectedLink is not null)
        {
            SelectedLink.IsSelected = true;
        }

        args.Handled = true;
    }

    [RelayCommand]
    private void DeleteSelectedItem()
    {
        if (SelectedLink is not null && !SelectedLink.IsImmutable)
        {
            linkViewModels.Remove(SelectedLink);
        }
        else if (SelectedPoint is not null && SelectedPoint.Immutability == Immutability.Modifiable)
        {
            var linksToDelete = LinkViewModels.Where(link => link.A == SelectedPoint || link.B == SelectedPoint).ToList();
            foreach (var link in linksToDelete)
            {
                linkViewModels.Remove(link);
            }

            jointViewModels.Remove(SelectedPoint);
            ClearSelections();
        }
    }

    [RelayCommand]
    private void CreateJoint()
    {
        jointViewModels.Add(new JointViewModel($"Point{pointNumber++}", JointType.Floating, 100, 100));
    }

    [RelayCommand]
    private void CreateLink()
    {
        var link = new LinkViewModel(null, null);
        linkViewModels.Add(link);
    }
}