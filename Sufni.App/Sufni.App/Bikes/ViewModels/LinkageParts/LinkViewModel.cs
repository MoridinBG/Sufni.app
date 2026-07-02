using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.ViewModels.LinkageParts;

public partial class LinkViewModel : ObservableObject
{
    public double? PixelsToMillimeters { get; set; }
    public bool IsImmutable => Name == "Shock";

    #region Observable properties

    [ObservableProperty] public partial JointViewModel? A { get; set; }
    [ObservableProperty] public partial JointViewModel? B { get; set; }
    [ObservableProperty] public partial Point StartPoint { get; set; }
    [ObservableProperty] public partial Point EndPoint { get; set; }
    [ObservableProperty] public partial string? Name { get; set; }
    [ObservableProperty] public partial double Length { get; set; }
    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial Brush Brush { get; set; } = new SolidColorBrush(Colors.CornflowerBlue);

    #endregion Observable properties

    #region Property change handlers

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
        if (sender is not JointViewModel point ||
            e.PropertyName is not (nameof(point.X) or nameof(point.Y))) return;
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

    #endregion Property change handlers

    #region Constructors

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

        UpdateLength();
    }

    #endregion Constructors

    #region Public methods

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
            Length = PixelsToMillimeters.Value * double.Hypot(dx, dy);
        }
    }

    public static LinkViewModel FromSpec(LinkSpec link, IEnumerable<JointViewModel> jointViewModels)
    {
        var jvms = jointViewModels as JointViewModel[] ?? jointViewModels.ToArray();
        var a = jvms.FirstOrDefault(j => j.Name == link.A);
        var b = jvms.FirstOrDefault(j => j.Name == link.B);

        var lvm = new LinkViewModel(a, b);
        lvm.UpdateLength();
        return lvm;
    }

    public LinkSpec ToSpec()
    {
        Debug.Assert(A is not null);
        Debug.Assert(B is not null);
        Debug.Assert(A.Name is not null);
        Debug.Assert(B.Name is not null);

        return new LinkSpec(A.Name, B.Name);
    }

    #endregion Public methods
}
