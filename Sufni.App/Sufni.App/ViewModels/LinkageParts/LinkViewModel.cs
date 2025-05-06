using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.Kinematics;

namespace Sufni.App.ViewModels.LinkageParts;

public partial class LinkViewModel : ViewModelBase
{
    [ObservableProperty] private JointViewModel? a;
    [ObservableProperty] private JointViewModel? b;
    [ObservableProperty] private Point startPoint;
    [ObservableProperty] private Point endPoint;
    [ObservableProperty] private string? name;
    [ObservableProperty] private double length;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private Brush brush = new SolidColorBrush(Colors.CornflowerBlue);
    public double? PixelsToMillimeters { get; set; }
    public bool IsImmutable => Name == "Shock";

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            Brush = new SolidColorBrush(Colors.RoyalBlue);
        }
        else
        {
            Brush = new SolidColorBrush(Colors.CornflowerBlue);
        }
    }

    partial void OnAChanged(JointViewModel? oldValue, JointViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnPointCoordinatesChanged;
        }
        if (newValue is not null)
        {
            newValue.PropertyChanged += OnPointCoordinatesChanged;
            StartPoint = new Point(newValue.X, newValue.Y);
            Name = $"{A?.Name} - {B?.Name}";
        }

        UpdateLength();
    }

    partial void OnBChanged(JointViewModel? oldValue, JointViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnPointCoordinatesChanged;
        }
        if (newValue is not null)
        {
            newValue.PropertyChanged += OnPointCoordinatesChanged;
            EndPoint = new Point(newValue.X, newValue.Y);
            Name = $"{A?.Name} - {B?.Name}";
        }

        UpdateLength();
    }

    private void OnPointCoordinatesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is JointViewModel point &&
            (e.PropertyName == nameof(point.X) || e.PropertyName == nameof(point.Y)))
        {
            if (point == A)
            {
                StartPoint = new Point(point.X, point.Y);
            }
            else
            {
                EndPoint = new Point(point.X, point.Y);
            }

            UpdateLength();
        }
    }

    public LinkViewModel(JointViewModel? a, JointViewModel? b, string? name = null)
    {
        if (a is not null)
        {
            A = a;
            StartPoint = new Point(a.X, a.Y);
            A.PropertyChanged += OnPointCoordinatesChanged;
        }

        if (b is not null)
        {
            B = b;
            EndPoint = new Point(b.X, b.Y);
            B.PropertyChanged += OnPointCoordinatesChanged;
        }
        if (name is null)
        {
            if (a is not null && b is not null)
            {
                Name = $"{a.Name} - {b.Name}";
            }
        }
        else
        {
            Name = name;
        }

        if (name is null)
        {
            if (a is not null && b is not null)
            {
                Name = $"{a.Name} - {b.Name}";
            }
        }
        else
        {
            Name = name;
        }

        UpdateLength();
    }

    public void UpdateLength(double? pixelsToMillimeter = null)
    {
        if (pixelsToMillimeter is not null)
        {
            PixelsToMillimeters = pixelsToMillimeter;
        }

        if (A is not null && B is not null && PixelsToMillimeters is not null)
        {
            var dx = B.X - A.X;
            var dy = B.Y - A.Y;
            Length = PixelsToMillimeters.Value * Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public static LinkViewModel FromLink(Link link, IEnumerable<JointViewModel> jointViewModels)
    {
        Debug.Assert(link.A is not null, "link.A is not null");
        Debug.Assert(link.B is not null, "link.A is not null");

        var a = jointViewModels.FirstOrDefault(j => j.Name == link.A.Name);
        var b = jointViewModels.FirstOrDefault(j => j.Name == link.B.Name);

        var lvm = new LinkViewModel(a, b);
        lvm.UpdateLength();
        return lvm;
    }

    public Link ToLink(double imageHeight, double pixelsToMillimeters)
    {
        Debug.Assert(A is not null, "A is not null");
        Debug.Assert(B is not null, "B is not null");
        Debug.Assert(A.Name is not null, "A.Name is not null");
        Debug.Assert(B.Name is not null, "B.Name is not null");

        return new Link(
            A.ToJoint(imageHeight, pixelsToMillimeters),
            B.ToJoint(imageHeight, pixelsToMillimeters));
    }
}
