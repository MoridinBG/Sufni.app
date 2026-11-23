using System;
using Avalonia.Controls;

namespace Sufni.App.Bikes.Views.Editors;

public partial class JointTypePicker : ComboBox
{
    protected override Type StyleKeyOverride => typeof(ComboBox);
    public JointTypePicker() => InitializeComponent();
}
