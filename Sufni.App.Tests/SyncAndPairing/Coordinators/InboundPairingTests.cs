using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using NSubstitute;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Tests.TestSupport.Sync;

namespace Sufni.App.Tests.SyncAndPairing.Coordinators;

public class InboundPairingTests
{
    [Theory]
    [InlineData("phone", "Phone", 0, true)]
    [InlineData("other", "Phone", 0, false)]
    [InlineData("phone", "Other", 0, false)]
    [InlineData("phone", "Phone", 30, false)]
    public async Task ConfirmPairingAsync_RequiresBoundIdentityNameAndUnexpiredPin(string id, string name, int elapsedSeconds, bool accepted)
    {
        await using var fixture = new InboundSyncFixture(new InlineUiThreadDispatcher());
        var coordinator = fixture.CreateCoordinator();
        PairingChallenge? prompt = null;
        using var subscription = coordinator.PairingPrompt.Subscribe(value => prompt = value);
        Assert.Equal(PairingCommandResult.Applied, await coordinator.RequestPairingAsync(new("phone", " Phone ")));
        var challenge = prompt!;
        fixture.Time.Advance(TimeSpan.FromSeconds(elapsedSeconds));

        var result = await coordinator.ConfirmPairingAsync(new(id, name, challenge.Pin));

        if (accepted)
        {
            var issued = Assert.IsType<PairingTokenResult.Issued>(result);
            var persisted = (await fixture.Database.PairedDeviceRepository.GetPairedDeviceAsync(id))!;
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(issued.Tokens.RefreshToken))), persisted.RefreshTokenHash);
            Assert.Equal("Phone", fixture.PairedDevices.Get(id)!.DisplayName);
            Assert.Null(prompt);
        }
        else
        {
            Assert.IsType<PairingTokenResult.Rejected>(result);
            Assert.Empty(await fixture.Database.PairedDeviceRepository.GetPairedDevicesAsync());
            Assert.Null(fixture.PairedDevices.Get("phone"));
        }
    }

    [Fact]
    public async Task ConfirmPairingAsync_DoesNotAcknowledgeBeforeDeviceAndPromptPublication()
    {
        var dispatcher = new ManualUiThreadDispatcher();
        await using var fixture = new InboundSyncFixture(dispatcher);
        var coordinator = fixture.CreateCoordinator();
        PairingChallenge? prompt = null;
        using var subscription = coordinator.PairingPrompt.Subscribe(value => prompt = value);
        var request = coordinator.RequestPairingAsync(new("phone", "Phone"));
        await dispatcher.DrainAsync();
        await request.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        var confirm = coordinator.ConfirmPairingAsync(new("phone", "Phone", prompt!.Pin));
        await dispatcher.WaitForWorkAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.False(confirm.IsCompleted);
        Assert.Null(fixture.PairedDevices.Get("phone"));
        await dispatcher.DrainAsync();
        Assert.IsType<PairingTokenResult.Issued>(await confirm.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));
        Assert.NotNull(fixture.PairedDevices.Get("phone"));
        Assert.Null(prompt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshPairingAsync_RecoversPreviousTokenOnce_UntilNewTokenIsUsed(bool recoverLostResponse)
    {
        await using var fixture = new InboundSyncFixture(new InlineUiThreadDispatcher());
        var coordinator = fixture.CreateCoordinator();
        var first = await PairAsync(coordinator);
        var previousExpiry = fixture.PairedDevices.Get("phone")!.Expires;
        fixture.Time.Advance(TimeSpan.FromDays(1));
        var second = Assert.IsType<PairingTokenResult.Issued>(await coordinator.RefreshPairingAsync(new(first.RefreshToken))).Tokens;
        Assert.True(fixture.PairedDevices.Get("phone")!.Expires > previousExpiry);
        var presented = recoverLostResponse ? first.RefreshToken : second.RefreshToken;

        var third = Assert.IsType<PairingTokenResult.Issued>(await coordinator.RefreshPairingAsync(new(presented))).Tokens;

        Assert.NotEqual(second.RefreshToken, third.RefreshToken);
        Assert.IsType<PairingTokenResult.Rejected>(await coordinator.RefreshPairingAsync(new(first.RefreshToken)));
        if (recoverLostResponse)
            Assert.IsType<PairingTokenResult.Rejected>(await coordinator.RefreshPairingAsync(new(second.RefreshToken)));
        Assert.IsType<PairingTokenResult.Issued>(await coordinator.RefreshPairingAsync(new(third.RefreshToken)));
    }

    [Fact]
    public async Task RefreshPairingAsync_ConcurrentRecoveryAcceptsPreviousTokenOnlyOnce()
    {
        await using var fixture = new InboundSyncFixture(new InlineUiThreadDispatcher());
        var coordinator = fixture.CreateCoordinator();
        var first = await PairAsync(coordinator);
        Assert.IsType<PairingTokenResult.Issued>(await coordinator.RefreshPairingAsync(new(first.RefreshToken)));

        var replies = await Task.WhenAll(coordinator.RefreshPairingAsync(new(first.RefreshToken)), coordinator.RefreshPairingAsync(new(first.RefreshToken)));

        Assert.Single(replies.OfType<PairingTokenResult.Issued>());
        Assert.Single(replies.OfType<PairingTokenResult.Rejected>());
    }

    [Fact]
    public async Task RefreshPairingAsync_RejectsExpiredCredential()
    {
        await using var fixture = new InboundSyncFixture(new InlineUiThreadDispatcher());
        var coordinator = fixture.CreateCoordinator();
        var tokens = await PairAsync(coordinator);
        fixture.Time.Advance(TimeSpan.FromDays(30));

        Assert.IsType<PairingTokenResult.Rejected>(await coordinator.RefreshPairingAsync(new(tokens.RefreshToken)));
    }

    [Fact]
    public async Task UnpairAsync_RequiresCredential_ThenRemovesPersistenceAndPublication()
    {
        await using var fixture = new InboundSyncFixture(new InlineUiThreadDispatcher());
        var coordinator = fixture.CreateCoordinator();
        var tokens = await PairAsync(coordinator);
        Assert.Equal(PairingCommandResult.Rejected, await coordinator.UnpairAsync(new("phone", "wrong")));
        Assert.NotNull(fixture.PairedDevices.Get("phone"));

        Assert.Equal(PairingCommandResult.Applied, await coordinator.UnpairAsync(new("phone", tokens.RefreshToken)));

        Assert.Null(await fixture.Database.PairedDeviceRepository.GetPairedDeviceAsync("phone"));
        Assert.Null(fixture.PairedDevices.Get("phone"));
    }

    [Fact]
    public async Task ConfirmPairingAsync_TokenFailureDoesNotPublishOrPersistAPair()
    {
        await using var fixture = new InboundSyncFixture(new InlineUiThreadDispatcher());
        var coordinator = fixture.CreateCoordinator();
        PairingChallenge? prompt = null;
        using var subscription = coordinator.PairingPrompt.Subscribe(value => prompt = value);
        await coordinator.RequestPairingAsync(new("phone", "Phone"));
        fixture.Tokens.CreateAccessTokenAsync("phone").Returns(Task.FromException<string>(new IOException("secure storage unavailable")));

        Assert.IsType<PairingTokenResult.Failed>(await coordinator.ConfirmPairingAsync(new("phone", "Phone", prompt!.Pin)));

        Assert.Null(await fixture.Database.PairedDeviceRepository.GetPairedDeviceAsync("phone"));
        Assert.Null(fixture.PairedDevices.Get("phone"));
    }

    private static async Task<TokenResponse> PairAsync(IInboundSyncCoordinator coordinator)
    {
        PairingChallenge? prompt = null;
        using var subscription = coordinator.PairingPrompt.Subscribe(value => prompt = value);
        await coordinator.RequestPairingAsync(new("phone", "Phone"));
        return Assert.IsType<PairingTokenResult.Issued>(await coordinator.ConfirmPairingAsync(new("phone", "Phone", prompt!.Pin))).Tokens;
    }
}
