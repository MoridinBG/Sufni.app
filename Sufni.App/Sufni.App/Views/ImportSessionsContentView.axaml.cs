using Avalonia;
using Avalonia.Controls;

namespace Sufni.App.Views;

public partial class ImportSessionsContentView : UserControl
{
    public static readonly StyledProperty<object?> BottomContentProperty =
        AvaloniaProperty.Register<ImportSessionsContentView, object?>(nameof(BottomContent));

    public static readonly StyledProperty<bool> EnableMalformedMessageTapProperty =
        AvaloniaProperty.Register<ImportSessionsContentView, bool>(nameof(EnableMalformedMessageTap));

    public object? BottomContent
    {
        get => GetValue(BottomContentProperty);
        set => SetValue(BottomContentProperty, value);
    }

    public bool EnableMalformedMessageTap
    {
        get => GetValue(EnableMalformedMessageTapProperty);
        set => SetValue(EnableMalformedMessageTapProperty, value);
    }

    public ImportSessionsContentView()
    {
        InitializeComponent();
    }
}
