using NSubstitute;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.ViewModels;

namespace Sufni.App.Tests.Segments.ViewModels;

public class StravaConnectDialogViewModelTests
{
    private readonly IStravaSegmentService stravaService = Substitute.For<IStravaSegmentService>();

    [Fact]
    public async Task Connect_WhenConnected_Completes()
    {
        stravaService.ConnectAsync(Arg.Any<StravaConnectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StravaConnectResult.Connected());
        var dialog = CreateDialogReadyToConnect();
        var completed = false;
        dialog.Completed += (_, _) => completed = true;

        await dialog.ConnectCommand.ExecuteAsync(null);

        Assert.True(completed);
        await stravaService.Received(1).ConnectAsync(
            new StravaConnectRequest("client", "secret", "http://localhost/exchange_token?code=abc"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Connect_WhenRejected_ShowsErrorAndStaysOpen()
    {
        stravaService.ConnectAsync(Arg.Any<StravaConnectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StravaConnectResult.MissingRequiredScope());
        var dialog = CreateDialogReadyToConnect();
        var completed = false;
        dialog.Completed += (_, _) => completed = true;

        await dialog.ConnectCommand.ExecuteAsync(null);

        Assert.True(dialog.HasError);
        Assert.False(completed);
    }

    [Fact]
    public void Connect_IsUnavailableUntilTheAuthorizationUrlWasOpened()
    {
        var dialog = new StravaConnectDialogViewModel(stravaService, new StravaConnectionSnapshot("client", "secret", IsConnected: false))
        {
            RedirectUrl = "http://localhost/exchange_token?code=abc"
        };

        Assert.False(dialog.ConnectCommand.CanExecute(null));
    }

    private StravaConnectDialogViewModel CreateDialogReadyToConnect()
    {
        var dialog = new StravaConnectDialogViewModel(stravaService, new StravaConnectionSnapshot("client", "secret", IsConnected: false));
        dialog.MarkAuthorizationUrlOpened();
        dialog.RedirectUrl = "http://localhost/exchange_token?code=abc";
        return dialog;
    }
}
