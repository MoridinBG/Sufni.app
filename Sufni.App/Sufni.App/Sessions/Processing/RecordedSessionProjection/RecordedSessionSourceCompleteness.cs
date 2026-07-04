using System;

namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

internal static class RecordedSessionSourceCompleteness
{
    public static bool IsSourceMissingOrHashMismatch(
        string? sourceHash,
        ProcessingFingerprint? persistedFingerprint)
    {
        if (string.IsNullOrWhiteSpace(sourceHash))
        {
            return true;
        }

        return persistedFingerprint is not null &&
               !StringComparer.Ordinal.Equals(sourceHash, persistedFingerprint.SourceHash);
    }
}
