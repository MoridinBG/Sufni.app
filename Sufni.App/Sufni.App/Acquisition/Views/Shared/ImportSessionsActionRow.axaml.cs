using Avalonia;
using Avalonia.Controls;

namespace Sufni.App.Acquisition.Views.Shared;

public partial class ImportSessionsActionRow : UserControl
{
    public static readonly StyledProperty<bool> ShowBackButtonProperty =
        AvaloniaProperty.Register<ImportSessionsActionRow, bool>(nameof(ShowBackButton));

    public bool ShowBackButton
    {
        get => GetValue(ShowBackButtonProperty);
        set => SetValue(ShowBackButtonProperty, value);
    }

    public ImportSessionsActionRow()
    {
        InitializeComponent();
    }
}
