using Avalonia;
using Avalonia.Controls;

namespace Sufni.App.Views.Shared;

public partial class SetupEditorCommonFields : UserControl
{
    public static readonly StyledProperty<bool> ShowBikeEditButtonProperty =
        AvaloniaProperty.Register<SetupEditorCommonFields, bool>(nameof(ShowBikeEditButton));

    public bool ShowBikeEditButton
    {
        get => GetValue(ShowBikeEditButtonProperty);
        set => SetValue(ShowBikeEditButtonProperty, value);
    }

    public SetupEditorCommonFields()
    {
        InitializeComponent();
    }
}
