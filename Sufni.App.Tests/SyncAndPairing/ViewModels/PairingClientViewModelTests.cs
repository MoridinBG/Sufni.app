using System.Reactive.Linq;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.ViewModels;

namespace Sufni.App.Tests.SyncAndPairing.ViewModels;

public class PairingClientViewModelTests
{
    private readonly IPairingClientCoordinator coordinator = Substitute.For<IPairingClientCoordinator>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();

    private PairingClientViewModel CreateViewModel()
    {
        coordinator.PairedState.Returns(Observable.Return(false));
        coordinator.DisplayName.Returns("Phone");
        return new PairingClientViewModel(coordinator, shell, Substitute.For<IDialogService>(),
            new InlineUiThreadDispatcher(), new FakeTimeProvider());
    }

    [Fact]
    public async Task ConfirmPairing_NavigatesBackAndClearsInput_OnlyAfterSuccess()
    {
        var vm = CreateViewModel();
        vm.Pin = "123456";
        vm.IsRequestSent = true;
        coordinator.ConfirmPairingAsync("Phone", vm.Pin).Returns(new ConfirmPairingResult.PinRejected());

        await vm.ConfirmPairingCommand.ExecuteAsync(null);

        Assert.True(vm.IsRequestSent);
        Assert.Equal("123456", vm.Pin);
        Assert.NotEmpty(vm.Notifications);
        Assert.Empty(vm.ErrorMessages);
        shell.DidNotReceive().GoBack();

        coordinator.ConfirmPairingAsync("Phone", vm.Pin).Returns(new ConfirmPairingResult.Paired());
        await vm.ConfirmPairingCommand.ExecuteAsync(null);

        Assert.False(vm.IsRequestSent);
        Assert.Null(vm.Pin);
        shell.Received(1).GoBack();
    }

    [Fact]
    public async Task RequestPairing_ReportsUnavailable_WithoutEnteringPinStep()
    {
        var vm = CreateViewModel();
        coordinator.RequestPairingAsync("Phone").Returns(new RequestPairingResult.ServerUnavailable());

        await vm.RequestPairingCommand.ExecuteAsync(null);

        Assert.False(vm.IsRequestSent);
        Assert.NotEmpty(vm.Notifications);
        Assert.Empty(vm.ErrorMessages);
    }

    [Theory]
    [InlineData(PairingFailureReason.Request)]
    [InlineData(PairingFailureReason.Confirm)]
    [InlineData(PairingFailureReason.Unpair)]
    public async Task FailedCommand_KeepsDiagnosticsOutOfSummary_AndStaysOnPage(PairingFailureReason reason)
    {
        const string details = "transport diagnostic";
        var vm = CreateViewModel();
        vm.Pin = "123456";
        coordinator.RequestPairingAsync("Phone").Returns(new RequestPairingResult.Failed(reason, details));
        coordinator.ConfirmPairingAsync("Phone", vm.Pin).Returns(new ConfirmPairingResult.Failed(reason, details));
        coordinator.UnpairAsync().Returns(new UnpairResult.Failed(reason, details));

        var command = reason switch
        {
            PairingFailureReason.Request => vm.RequestPairingCommand,
            PairingFailureReason.Confirm => vm.ConfirmPairingCommand,
            PairingFailureReason.Unpair => vm.UnpairCommand,
            _ => throw new ArgumentOutOfRangeException(nameof(reason))
        };
        await command.ExecuteAsync(null);

        var failure = Assert.Single(vm.ErrorMessages);
        Assert.Equal(details, failure.Details);
        Assert.DoesNotContain(details, failure.Summary);
        Assert.Empty(vm.Notifications);
        shell.DidNotReceive().GoBack();
    }
}
