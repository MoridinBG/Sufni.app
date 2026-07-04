using System;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

internal static class RecordedSessionDerivationResolver
{
    public static Guid GetEffectiveSourceSessionId(Guid sessionId, RecordedSessionDerivationWindow? window)
    {
        var normalized = NormalizeWindow(window);
        return normalized?.SourceSessionId ?? sessionId;
    }

    public static Guid GetEffectiveSourceSessionId(Guid sessionId, ProcessingFingerprint? fingerprint) =>
        GetEffectiveSourceSessionId(sessionId, fingerprint?.DerivationWindow);

    public static RecordedSessionDerivationWindow? NormalizeWindow(RecordedSessionDerivationWindow? window) =>
        window is null || window.SourceSessionId == Guid.Empty
            ? null
            : window;
}
