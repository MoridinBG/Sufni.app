using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views;

namespace Sufni.App.Tests.Services;

public class DialogServiceTests
{
    [AvaloniaFact]
    public async Task ShowDialogAsync_DesktopMode_UsesOwnedWindowAndReturnsCompletionResult()
    {
        TestApp.SetIsDesktop(true);

        var service = new DialogService();
        var owner = new Window();
        var viewModel = new TestExtensionDialogResultSource<string>();
        owner.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        service.SetOwner(owner);

        try
        {
            var resultTask = service.ShowDialogAsync(CreateRequest("Extension Dialog", viewModel));
            await ViewTestHelpers.FlushDispatcherAsync();

            var dialog = Assert.Single(owner.OwnedWindows);
            Assert.Equal("Extension Dialog", dialog.Title);
            Assert.Equal(320, dialog.Width);
            Assert.Equal(240, dialog.Height);
            Assert.Equal(200, dialog.MinWidth);
            Assert.Equal(160, dialog.MinHeight);
            Assert.False(dialog.CanResize);

            var content = Assert.IsType<ContentControl>(dialog.Content);
            Assert.Same(viewModel, content.Content);

            viewModel.Complete("done");

            var result = await resultTask;
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Equal("done", result);
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            CloseOwnedWindows(owner);
            owner.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task ShowDialogAsync_DesktopMode_ReturnsDefault_WhenWindowClosesWithoutCompletion()
    {
        TestApp.SetIsDesktop(true);

        var service = new DialogService();
        var owner = new Window();
        var viewModel = new TestExtensionDialogResultSource<string>();
        owner.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        service.SetOwner(owner);

        try
        {
            var resultTask = service.ShowDialogAsync(CreateRequest("Extension Dialog", viewModel));
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Single(owner.OwnedWindows).Close();

            var result = await resultTask;
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Null(result);
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            CloseOwnedWindows(owner);
            owner.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task ShowDialogAsync_MobileMode_UsesOverlayAndReturnsCompletionResult()
    {
        TestApp.SetIsDesktop(false);

        var service = new DialogService();
        var overlayHost = new Grid();
        var owner = ViewTestHelpers.ShowView(overlayHost);
        var viewModel = new TestExtensionDialogResultSource<string>();
        await ViewTestHelpers.FlushDispatcherAsync();

        service.SetOwner(owner);
        service.SetOverlayHost(overlayHost);

        try
        {
            var resultTask = service.ShowDialogAsync(CreateRequest("Extension Dialog", viewModel));
            await ViewTestHelpers.FlushDispatcherAsync();

            var content = FindExtensionDialogContent(overlayHost, viewModel);
            Assert.NotNull(content);
            Assert.Empty(owner.OwnedWindows);

            viewModel.Complete("done");

            var result = await resultTask;
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Equal("done", result);
            Assert.Null(FindExtensionDialogContent(overlayHost, viewModel));
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            owner.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task ShowDialogAsync_MobileMode_ReturnsDefault_WhenOverlayClosesWithoutCompletion()
    {
        TestApp.SetIsDesktop(false);

        var service = new DialogService();
        var overlayHost = new Grid();
        var owner = ViewTestHelpers.ShowView(overlayHost);
        var viewModel = new TestExtensionDialogResultSource<string>();
        await ViewTestHelpers.FlushDispatcherAsync();

        service.SetOwner(owner);
        service.SetOverlayHost(overlayHost);

        try
        {
            var resultTask = service.ShowDialogAsync(CreateRequest("Extension Dialog", viewModel));
            await ViewTestHelpers.FlushDispatcherAsync();

            var closeButton = overlayHost.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Name == "ExtensionDialogCloseButton");
            closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var result = await resultTask;
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Null(result);
            Assert.Null(FindExtensionDialogContent(overlayHost, viewModel));
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            owner.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

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

    private static ExtensionDialogRequest<string> CreateRequest(
        string title,
        TestExtensionDialogResultSource<string> viewModel) =>
        new(
            title,
            viewModel,
            new ExtensionDialogLayout(
                Width: 320,
                Height: 240,
                MinWidth: 200,
                MinHeight: 160,
                CanResize: false));

    private static ContentControl? FindExtensionDialogContent(
        Control root,
        TestExtensionDialogResultSource<string> viewModel)
    {
        return root.GetVisualDescendants()
            .OfType<ContentControl>()
            .SingleOrDefault(control => ReferenceEquals(control.Content, viewModel));
    }

    private sealed class TestExtensionDialogResultSource<TResult> : IExtensionDialogResultSource<TResult>
    {
        public event EventHandler<TResult?>? Completed;

        public void Complete(TResult? result)
        {
            Completed?.Invoke(this, result);
        }
    }
}
