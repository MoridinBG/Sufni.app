using Sufni.App.Shared.Models;
using NSubstitute;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.ViewModels;

namespace Sufni.App.Tests.Segments.ViewModels;

public class StravaConnectDialogViewModelTests
{
    private readonly ISegmentCoordinator segmentCoordinator = Substitute.For<ISegmentCoordinator>();

    [Fact]
    public async Task Connect_WhenConnected_Completes()
    {
        segmentCoordinator.ConnectStravaAsync(Arg.Any<StravaConnectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StravaConnectResult.Connected());
        var dialog = CreateDialogReadyToConnect();
        var completed = false;
        dialog.Completed += (_, _) => completed = true;

        await dialog.ConnectCommand.ExecuteAsync(null);

        Assert.True(completed);
        await segmentCoordinator.Received(1).ConnectStravaAsync(
            new StravaConnectRequest("client", "secret", "http://localhost/exchange_token?code=abc"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Connect_WhenRejected_ShowsErrorAndStaysOpen()
    {
        segmentCoordinator.ConnectStravaAsync(Arg.Any<StravaConnectRequest>(), Arg.Any<CancellationToken>())
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
        var dialog = new StravaConnectDialogViewModel(segmentCoordinator, new StravaConnectionSnapshot("client", "secret", IsConnected: false))
        {
            RedirectUrl = "http://localhost/exchange_token?code=abc"
        };

        Assert.False(dialog.ConnectCommand.CanExecute(null));
    }

    [Fact]
    public async Task Connect_WhenFailed_KeepsDiagnosticsSeparateAndAllowsRetry()
    {
        const string details = "technical diagnostic";
        segmentCoordinator.ConnectStravaAsync(Arg.Any<StravaConnectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StravaConnectResult.Failed(StravaConnectFailureReason.Connect, details),
                new StravaConnectResult.Connected());
        var dialog = CreateDialogReadyToConnect();
        var completed = false;
        dialog.Completed += (_, _) => completed = true;

        await dialog.ConnectCommand.ExecuteAsync(null);

        Assert.False(completed);
        Assert.NotNull(dialog.Error);
        Assert.Equal(details, dialog.Error.Details);
        Assert.DoesNotContain(details, dialog.Error.Summary);
        Assert.True(dialog.ConnectCommand.CanExecute(null));

        await dialog.ConnectCommand.ExecuteAsync(null);

        Assert.True(completed);
        Assert.Null(dialog.Error);
    }

    private StravaConnectDialogViewModel CreateDialogReadyToConnect()
    {
        var dialog = new StravaConnectDialogViewModel(segmentCoordinator, new StravaConnectionSnapshot("client", "secret", IsConnected: false));
        dialog.MarkAuthorizationUrlOpened();
        dialog.RedirectUrl = "http://localhost/exchange_token?code=abc";
        return dialog;
    }
}
