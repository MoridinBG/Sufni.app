using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

using Sufni.App.Segments.ViewModels;
namespace Sufni.App.Segments.Views;

public partial class StravaConnectDialogView : UserControl
{
    public StravaConnectDialogView()
    {
        InitializeComponent();
    }

    private async void AuthorizationButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StravaConnectDialogViewModel dialog ||
            !Uri.TryCreate(dialog.AuthorizationUrl, UriKind.Absolute, out var uri) ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        if (await topLevel.Launcher.LaunchUriAsync(uri))
        {
            dialog.MarkAuthorizationUrlOpened();
        }
    }
}
