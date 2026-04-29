using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AvaloniaProgressRing;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.Views;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views;

public class PairingClientViewTests
{
    [AvaloniaFact]
    public async Task PairingClientView_ShowsLocatingState_AndStartsBrowsing_WhenNoServerIsFound()
    {
        var coordinator = CreateCoordinator(displayName: "Phone", serverUrl: null, isPaired: false);
        var shell = Substitute.For<IShellCoordinator>();
        var viewModel = new PairingClientViewModel(coordinator, shell);

        await using (var mounted = await MountAsync(viewModel))
        {
            var locatingLabel = mounted.View.FindControl<Label>("LocatingServersLabel");
            var progressRing = mounted.View.FindControl<ProgressRing>("BrowseProgressRing");
            var displayName = mounted.View.FindControl<TextBox>("DisplayNameTextBox");
            var requestButton = mounted.View.FindControl<Button>("RequestPairingButton");
            var pinInput = mounted.View.FindControl<PinInput>("PinInputControl");
            var unpairButton = mounted.View.FindControl<Button>("UnpairButton");

            Assert.NotNull(locatingLabel);
            Assert.NotNull(progressRing);
            Assert.NotNull(displayName);
            Assert.NotNull(requestButton);
            Assert.NotNull(pinInput);
            Assert.NotNull(unpairButton);

            Assert.True(locatingLabel!.IsVisible);
            Assert.True(progressRing!.IsActive);
            Assert.True(displayName!.IsEnabled);
            Assert.False(requestButton!.IsVisible);
            Assert.False(pinInput!.IsVisible);
            Assert.False(unpairButton!.IsVisible);
            coordinator.Received(1).StartBrowsing();
        }

        coordinator.Received(1).StopBrowsing();
    }

    [AvaloniaFact]
    public async Task PairingClientView_ShowsPinInputAndLocksDisplayName_AfterSuccessfulRequest()
    {
        var coordinator = CreateCoordinator(displayName: "Phone", serverUrl: "https://desktop", isPaired: false);
        coordinator.RequestPairingAsync("Phone").Returns(new RequestPairingResult.Sent());
        var shell = Substitute.For<IShellCoordinator>();
        var viewModel = new PairingClientViewModel(coordinator, shell);

        await using var mounted = await MountAsync(viewModel);

        var displayName = mounted.View.FindControl<TextBox>("DisplayNameTextBox");
        var requestButton = mounted.View.FindControl<Button>("RequestPairingButton");
        var pinInput = mounted.View.FindControl<PinInput>("PinInputControl");

        Assert.NotNull(displayName);
        Assert.NotNull(requestButton);
        Assert.NotNull(pinInput);
        Assert.True(displayName!.IsEnabled);
        Assert.True(requestButton!.IsVisible);
        Assert.False(pinInput!.IsVisible);

        requestButton.Command!.Execute(requestButton.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(viewModel.IsRequestSent);
        Assert.False(displayName.IsEnabled);
        Assert.False(requestButton.IsVisible);
        Assert.True(pinInput.IsVisible);
        await coordinator.Received(1).RequestPairingAsync("Phone");
    }

    [AvaloniaFact]
    public async Task PairingClientView_ShowsUnpairState_WhenAlreadyPaired()
    {
        var coordinator = CreateCoordinator(displayName: "Phone", serverUrl: null, isPaired: true);
        var shell = Substitute.For<IShellCoordinator>();
        var viewModel = new PairingClientViewModel(coordinator, shell);

        await using var mounted = await MountAsync(viewModel);

        var locatingLabel = mounted.View.FindControl<Label>("LocatingServersLabel");
        var progressRing = mounted.View.FindControl<ProgressRing>("BrowseProgressRing");
        var displayName = mounted.View.FindControl<TextBox>("DisplayNameTextBox");
        var requestButton = mounted.View.FindControl<Button>("RequestPairingButton");
        var unpairButton = mounted.View.FindControl<Button>("UnpairButton");

        Assert.NotNull(locatingLabel);
        Assert.NotNull(progressRing);
        Assert.NotNull(displayName);
        Assert.NotNull(requestButton);
        Assert.NotNull(unpairButton);
        Assert.False(locatingLabel!.IsVisible);
        Assert.False(progressRing!.IsActive);
        Assert.False(displayName!.IsEnabled);
        Assert.False(requestButton!.IsVisible);
        Assert.True(unpairButton!.IsVisible);
    }

    [AvaloniaFact]
    public async Task PairingClientView_ShowsErrorBar_WhenRequestFails()
    {
        var coordinator = CreateCoordinator(displayName: "Phone", serverUrl: "https://desktop", isPaired: false);
        coordinator.RequestPairingAsync("Phone").Returns(new RequestPairingResult.Failed("network"));
        var shell = Substitute.For<IShellCoordinator>();
        var viewModel = new PairingClientViewModel(coordinator, shell);

        await using var mounted = await MountAsync(viewModel);

        var requestButton = mounted.View.FindControl<Button>("RequestPairingButton");
        var errorBar = mounted.View.GetVisualDescendants().OfType<ErrorMessagesBar>().Single();

        Assert.NotNull(requestButton);
        requestButton!.Command!.Execute(requestButton.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Contains("Could not request pairing: network", viewModel.ErrorMessages);
        Assert.True(errorBar.IsVisible);
        Assert.Equal(1, Grid.GetRow(errorBar));
    }

    [AvaloniaFact]
    public async Task PairingClientView_ShowsNotificationBar_WhenUnpairFallsBackToLocalOnly()
    {
        var coordinator = CreateCoordinator(displayName: "Phone", serverUrl: null, isPaired: true);
        coordinator.UnpairAsync().Returns(new UnpairResult.LocalOnly("remote offline"));
        var shell = Substitute.For<IShellCoordinator>();
        var viewModel = new PairingClientViewModel(coordinator, shell);

        await using var mounted = await MountAsync(viewModel);

        var unpairButton = mounted.View.FindControl<Button>("UnpairButton");
        var notificationsBar = mounted.View.GetVisualDescendants().OfType<NotificationsBar>().Single();

        Assert.NotNull(unpairButton);
        unpairButton!.Command!.Execute(unpairButton.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Contains("Could unpair only locally: remote offline", viewModel.Notifications);
        Assert.True(notificationsBar.IsVisible);
    }

    private static IPairingClientCoordinator CreateCoordinator(string? displayName, string? serverUrl, bool isPaired)
    {
        var coordinator = Substitute.For<IPairingClientCoordinator>();
        coordinator.DisplayName.Returns(displayName);
        coordinator.ServerUrl.Returns(serverUrl);
        coordinator.IsPaired.Returns(isPaired);
        coordinator.RequestPairingAsync(Arg.Any<string?>()).Returns(new RequestPairingResult.Sent());
        coordinator.UnpairAsync().Returns(new UnpairResult.Unpaired());
        return coordinator;
    }

    private static async Task<MountedPairingClientView> MountAsync(PairingClientViewModel viewModel)
    {
        ViewTestHelpers.EnsureViewTestResources();

        var view = new PairingClientView
        {
            DataContext = viewModel,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedPairingClientView(host, view);
    }
}

internal sealed class MountedPairingClientView(Window host, PairingClientView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public PairingClientView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}