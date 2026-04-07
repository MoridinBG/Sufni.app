using System;
using System.Threading.Tasks;

namespace Sufni.App.Coordinators;

/// <summary>
/// Owns the mobile pairing-client workflow. Mobile-only — registered as
/// a singleton in <c>Sufni.App.iOS/AppDelegate.cs</c> and
/// <c>Sufni.App.Android/MainActivity.cs</c>. Holds the canonical
/// device id, the latest discovered server URL, and the
/// <see cref="IsPaired"/> source of truth. Service-discovery
/// browsing has the lifetime of the coordinator (and thus of the
/// app), so the underlying mDNS subscription is started and stopped
/// via <see cref="StartBrowsing"/> / <see cref="StopBrowsing"/> from
/// the pairing client view's Loaded/Unloaded.
/// </summary>
public interface IPairingClientCoordinator
{
    /// <summary>
    /// Stable, opaque device id loaded from secure storage on
    /// construction. Generated as a fresh <see cref="Guid"/> on first
    /// run. Not user-editable — the human-readable label lives on
    /// <see cref="DisplayName"/>.
    /// </summary>
    string? DeviceId { get; }

    /// <summary>
    /// Optional human-readable label for this device. Loaded from
    /// secure storage if previously committed; otherwise defaults to
    /// the platform's friendly name (which may itself be null).
    /// The VM keeps an editable copy bound to the text box; the
    /// coordinator's value only persists when the user successfully
    /// pairs with a (possibly modified) display name.
    /// </summary>
    string? DisplayName { get; }

    /// <summary>
    /// Latest discovered server URL, null when no server is currently
    /// visible on the network.
    /// </summary>
    string? ServerUrl { get; }

    /// <summary>
    /// True when the client has a stored refresh token and the last
    /// /pair/refresh round-trip succeeded (or hasn't been attempted
    /// yet but a token exists).
    /// </summary>
    bool IsPaired { get; }

    event EventHandler? DeviceIdChanged;
    event EventHandler? DisplayNameChanged;
    event EventHandler? ServerUrlChanged;
    event EventHandler? IsPairedChanged;

    /// <summary>
    /// Fires after <see cref="ConfirmPairingAsync"/> successfully
    /// pairs with a server. Distinct from <see cref="IsPairedChanged"/>,
    /// which also fires on the startup <c>IsPairedAsync</c> probe —
    /// this event only fires for a fresh, user-initiated pair.
    /// </summary>
    event EventHandler? PairingConfirmed;

    void StartBrowsing();
    void StopBrowsing();

    Task<RequestPairingResult> RequestPairingAsync(string? displayName);
    Task<ConfirmPairingResult> ConfirmPairingAsync(string? displayName, string pin);
    Task<UnpairResult> UnpairAsync();
}

public abstract record RequestPairingResult
{
    private RequestPairingResult() { }
    public sealed record Sent : RequestPairingResult;
    public sealed record Failed(string ErrorMessage) : RequestPairingResult;
}

public abstract record ConfirmPairingResult
{
    private ConfirmPairingResult() { }
    public sealed record Paired : ConfirmPairingResult;
    public sealed record Failed(string ErrorMessage) : ConfirmPairingResult;
}

public abstract record UnpairResult
{
    private UnpairResult() { }
    public sealed record Unpaired : UnpairResult;
    // Network call failed but local credentials were already cleared,
    // matching the existing "could unpair only locally" behaviour.
    public sealed record LocalOnly(string Reason) : UnpairResult;
    public sealed record Failed(string ErrorMessage) : UnpairResult;
}
