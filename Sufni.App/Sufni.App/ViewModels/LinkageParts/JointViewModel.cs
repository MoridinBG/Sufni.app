using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ViewModels.Items;
using Sufni.Kinematics;

namespace Sufni.App.ViewModels.LinkageParts;

public enum Immutability
{
    Immutable,
    NameOnly,
    Modifiable
}

public partial class JointViewModel : ViewModelBase
{
    [ObservableProperty] private double x;
    [ObservableProperty] private double y;
    [ObservableProperty] private string name;
    [ObservableProperty] private JointType type;
    public static ObservableCollection<JointType> PointTypes { get; } = [JointType.Fixed, JointType.Floating];
    [ObservableProperty] private Brush brush;
    [ObservableProperty] private bool isSelected;

    public Immutability Immutability { get; private set; }

    partial void OnTypeChanged(JointType value)
    {
        Brush = BikeViewModel.PointTypeToBrushMapping[value];
    }

    public JointViewModel(string name, JointType type, double x, double y)
    {
        X = x;
        Y = y;
        Name = name;
        Type = type;
        Brush = BikeViewModel.PointTypeToBrushMapping[type];

        if (Type == JointType.FrontWheel || Type == JointType.RearWheel || Type == JointType.BottomBracket)
        {
            Immutability = Immutability.Immutable;
        }
        else if (Name == "Shock eye 1" || Name == "Shock eye 2")
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
        Debug.Assert(joint.Name is not null, "joint.Name is not null");
        Debug.Assert(joint.Type is not null, "joint.Type is not null");

        var x = joint.X / pixelsToMillimeters;
        var y = imageHeight - joint.Y / pixelsToMillimeters;
        return new(joint.Name, joint.Type.Value, x, y);
    }

    public Joint ToJoint(double imageHeight, double pixelsToMillimeters)
    {
        var x = X * pixelsToMillimeters;
        var y = (imageHeight - Y) * pixelsToMillimeters;
        return new Joint(Name, Type, x, y);
    }
}
