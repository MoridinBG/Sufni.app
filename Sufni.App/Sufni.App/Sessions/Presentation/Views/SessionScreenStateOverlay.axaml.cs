using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Sufni.App.Sessions.Presentation.Views;

internal partial class SessionScreenStateOverlay : UserControl
{
    public static readonly StyledProperty<SessionScreenPresentationState> ScreenStateProperty =
        AvaloniaProperty.Register<SessionScreenStateOverlay, SessionScreenPresentationState>(
            nameof(ScreenState), SessionScreenPresentationState.Ready);

    public static readonly StyledProperty<string?> LoadingFallbackMessageProperty =
        AvaloniaProperty.Register<SessionScreenStateOverlay, string?>(nameof(LoadingFallbackMessage));

    public static readonly FuncMultiValueConverter<object?, string?> LoadingMessageConverter =
        new(values => values.OfType<string>().FirstOrDefault());

    public SessionScreenPresentationState ScreenState
    {
        get => GetValue(ScreenStateProperty);
        set => SetValue(ScreenStateProperty, value);
    }

    public string? LoadingFallbackMessage
    {
        get => GetValue(LoadingFallbackMessageProperty);
        set => SetValue(LoadingFallbackMessageProperty, value);
    }

    public SessionScreenStateOverlay() => InitializeComponent();
}
