using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.Kinematics;

namespace Sufni.App.ViewModels.LinkageParts;

public enum Immutability
{
    Immutable,
    NameOnly,
    Modifiable
}

public partial class JointViewModel : ViewModelBase, IPoint
{
    public Immutability Immutability { get; private set; }
    public bool ShowFlyout { get; set; }

    #region Private fields

    private static Dictionary<JointType, Brush> TypeToBrushMapping { get; } = new()
    {
        { JointType.RearWheel, new SolidColorBrush(Colors.Cyan)},
        { JointType.FrontWheel, new SolidColorBrush(Colors.Cyan)},
        { JointType.Fixed, new SolidColorBrush(Colors.OrangeRed)},
        { JointType.Floating, new SolidColorBrush(Colors.HotPink)},
        { JointType.BottomBracket, new SolidColorBrush(Colors.Purple)}
    };

    #endregion Private fields

    #region Observable properties

    [ObservableProperty] private double x;
    [ObservableProperty] private double y;
    [ObservableProperty] private string name;
    [ObservableProperty] private JointType type;
    public static ObservableCollection<JointType> PointTypes { get; } = [JointType.Fixed, JointType.Floating];
    [ObservableProperty] private Brush brush;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool wasPossiblyDragged;

    #endregion Observable properties

    #region Property change handlers

    partial void OnTypeChanged(JointType value)
    {
        Brush = TypeToBrushMapping[value];
    }

    #endregion Property change handlers

    #region Constructors / Initializers

    public JointViewModel(string name, JointType type, double x, double y, bool showFlyout = false)
    {
        X = x;
        Y = y;
        Name = name;
        Type = type;
        Brush = TypeToBrushMapping[type];
        ShowFlyout = showFlyout;

        if (Type is JointType.FrontWheel or JointType.RearWheel or JointType.BottomBracket)
        {
            Immutability = Immutability.Immutable;
        }
        else if (Name is "Shock eye 1" or "Shock eye 2")
        {
            Immutability = Immutability.NameOnly;
        }
        else
        {
            Immutability = Immutability.Modifiable;
        }
    }

    public static JointViewModel FromJoint(Joint joint, double imageHeight, double pixelsToMillimeters)
    {
        Debug.Assert(joint.Name is not null);
        Debug.Assert(joint.Type is not null);

        var x = joint.X / pixelsToMillimeters;
        var y = imageHeight - joint.Y / pixelsToMillimeters;
        return new JointViewModel(joint.Name, joint.Type.Value, x, y);
    }

    #endregion Constructors / Initializers

    #region Public methods

    public Joint ToJoint(double imageHeight, double pixelsToMillimeters)
    {
        return new Joint(Name, Type, X * pixelsToMillimeters, (imageHeight - Y) * pixelsToMillimeters);
    }

    #endregion Public methods
}
