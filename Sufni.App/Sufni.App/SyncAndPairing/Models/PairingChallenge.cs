using System;

namespace Sufni.App.SyncAndPairing.Models;

public sealed record PairingChallenge(string DeviceId, string? DisplayName, string Pin, DateTimeOffset ExpiresAt);

public enum PairingCommandResult { Applied, Rejected, Failed }

public abstract record PairingTokenResult
{
    private PairingTokenResult() { }
    public sealed record Issued(TokenResponse Tokens) : PairingTokenResult;
    public sealed record Rejected : PairingTokenResult;
    public sealed record Failed : PairingTokenResult;
}
