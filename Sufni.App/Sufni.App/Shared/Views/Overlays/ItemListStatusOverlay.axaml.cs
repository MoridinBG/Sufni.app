using Avalonia;
using Avalonia.Controls;

namespace Sufni.App.Shared.Views.Overlays;

public partial class ItemListStatusOverlay : UserControl
{
    public static readonly StyledProperty<bool> ShowUndoDeleteButtonProperty =
        AvaloniaProperty.Register<ItemListStatusOverlay, bool>(nameof(ShowUndoDeleteButton), true);

    public bool ShowUndoDeleteButton
    {
        get => GetValue(ShowUndoDeleteButtonProperty);
        set => SetValue(ShowUndoDeleteButtonProperty, value);
    }

    public ItemListStatusOverlay()
    {
        InitializeComponent();
    }
}
