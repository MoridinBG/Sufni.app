using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.ViewModels.LinkageParts;

public enum Immutability
{
    Immutable,
    NameOnly,
    Modifiable
}

public partial class JointViewModel : ObservableObject, IPoint
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
        { JointType.BottomBracket, new SolidColorBrush(Colors.Purple)},
        { JointType.HeadTube, new SolidColorBrush(Colors.Lime)}
    };

    #endregion Private fields

    #region Observable properties

    [ObservableProperty] public partial double X { get; set; }
    [ObservableProperty] public partial double Y { get; set; }
    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial JointType? Type { get; set; }
    public static ObservableCollection<JointType?> PointTypes { get; } = [null, JointType.Fixed, JointType.Floating, JointType.HeadTube];
    [ObservableProperty] public partial Brush Brush { get; set; }
    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial bool WasPossiblyDragged { get; set; }

    #endregion Observable properties

    #region Property change handlers

    partial void OnTypeChanged(JointType? value)
    {
        Brush = BrushFor(value);
    }

    #endregion Property change handlers

    #region Constructors / Initializers

    public JointViewModel(string name, JointType? type, double x, double y, bool showFlyout = false)
    {
        X = x;
        Y = y;
        Name = name;
        Type = type;
        Brush = BrushFor(type);
        ShowFlyout = showFlyout;

        if (Type is JointType.FrontWheel or JointType.RearWheel or JointType.BottomBracket or JointType.HeadTube)
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

    public static JointViewModel FromSpec(JointSpec joint, double imageHeight, double pixelsToMillimeters)
    {
        var x = joint.X / pixelsToMillimeters;
        var y = imageHeight - joint.Y / pixelsToMillimeters;
        return new JointViewModel(joint.Name, joint.Type, x, y);
    }

    #endregion Constructors / Initializers

    #region Public methods

    public JointSpec ToSpec(double imageHeight, double pixelsToMillimeters)
    {
        return new JointSpec(Name, Type, X * pixelsToMillimeters, (imageHeight - Y) * pixelsToMillimeters);
    }

    private static Brush BrushFor(JointType? type) =>
        type is null
            ? TypeToBrushMapping[JointType.Floating]
            : TypeToBrushMapping[type.Value];

    #endregion Public methods
}
