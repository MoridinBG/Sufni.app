using System.Threading.Tasks;
using Sufni.App.Stores;

namespace Sufni.App.Coordinators;

/// <summary>
/// Owns the paired-device feature workflow. Subscribes to the
/// synchronization server's pairing events in its constructor and
/// keeps the <see cref="IPairedDeviceStore"/> in sync. Registered as a
/// singleton; eagerly resolved at app startup so the constructor's
/// event subscriptions actually run.
///
/// Paired devices have no detail tab and are not editable, so there is
/// no <c>OpenAsync</c> / <c>SaveAsync</c> / conflict protocol — only
/// <see cref="UnpairAsync"/>.
/// </summary>
public interface IPairedDeviceCoordinator
{
    /// <summary>
    /// Local-only unpair: deletes the row from the database and removes
    /// it from the store. Does <b>not</b> notify the mobile client —
    /// that is preserved verbatim from the legacy code. The mobile
    /// client discovers the unpair on its next sync attempt.
    /// </summary>
    Task<PairedDeviceUnpairResult> UnpairAsync(string deviceId);
}

/// <summary>
/// Outcome of <see cref="IPairedDeviceCoordinator.UnpairAsync"/>.
/// Sealed hierarchy with a private constructor so callers must
/// pattern-match on the two known cases.
/// </summary>
public abstract record PairedDeviceUnpairResult
{
    private PairedDeviceUnpairResult() { }

    public sealed record Unpaired : PairedDeviceUnpairResult;
    public sealed record Failed(string ErrorMessage) : PairedDeviceUnpairResult;
}
