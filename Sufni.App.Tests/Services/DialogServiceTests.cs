using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views;

namespace Sufni.App.Tests.Services;

public class DialogServiceTests
{
    [AvaloniaFact]
    public async Task ShowAddTileLayerDialogAsync_DesktopMode_UsesOwnedWindow()
    {
        TestApp.SetIsDesktop(true);

        var service = new DialogService();
        var owner = new Window();
        owner.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        service.SetOwner(owner);

        try
        {
            var resultTask = service.ShowAddTileLayerDialogAsync();
            await ViewTestHelpers.FlushDispatcherAsync();

            var dialog = Assert.Single(owner.OwnedWindows);
            var content = Assert.IsType<AddTileLayerView>(dialog.Content);

            SubmitLayer(content, "Trail", "https://tiles.example/{z}/{x}/{y}.png");

            var result = await resultTask;

            Assert.NotNull(result);
            Assert.Equal("Trail", result!.Name);
            Assert.Equal("https://tiles.example/{z}/{x}/{y}.png", result.UrlTemplate);
        }
        finally
        {
            CloseOwnedWindows(owner);
            owner.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task ShowAddTileLayerDialogAsync_MobileMode_UsesOverlayHost()
    {
        TestApp.SetIsDesktop(false);

        var service = new DialogService();
        var overlayHost = new Grid();
        var owner = ViewTestHelpers.ShowView(overlayHost);
        await ViewTestHelpers.FlushDispatcherAsync();

        service.SetOwner(owner);
        service.SetOverlayHost(overlayHost);

        try
        {
            var resultTask = service.ShowAddTileLayerDialogAsync();
            await ViewTestHelpers.FlushDispatcherAsync();

            var content = overlayHost.FindFirstVisual<AddTileLayerView>();
            Assert.NotNull(content);

            Assert.Empty(owner.OwnedWindows);

            SubmitLayer(content!, "Offline", "https://offline.example/{z}/{x}/{y}.png");

            var result = await resultTask;
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.NotNull(result);
            Assert.Equal("Offline", result!.Name);
            Assert.Equal("https://offline.example/{z}/{x}/{y}.png", result.UrlTemplate);
            Assert.Null(overlayHost.FindFirstVisual<AddTileLayerView>());
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            owner.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    private static void SubmitLayer(AddTileLayerView view, string name, string urlTemplate)
    {
        view.FindControl<TextBox>("NameBox")!.Text = name;
        view.FindControl<TextBox>("UrlTemplateBox")!.Text = urlTemplate;

        var okButton = view.FindControl<Button>("OkButton");
        Assert.NotNull(okButton);
        okButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void CloseOwnedWindows(Window owner)
    {
        foreach (var ownedWindow in owner.OwnedWindows.ToArray())
        {
            ownedWindow.Close();
        }
    }
}