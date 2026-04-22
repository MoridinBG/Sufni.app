using System;
using System.Security.Cryptography.X509Certificates;

namespace Sufni.App.Services;

internal static class SynchronizationCertificateValidator
{
    public static bool TryValidate(
        X509Certificate2? certificate,
        string? requiredThumbprint,
        DateTimeOffset now,
        out string? observedThumbprint)
    {
        observedThumbprint = null;

        if (certificate is null)
        {
            return false;
        }

        if (certificate.NotBefore > now || certificate.NotAfter < now)
        {
            return false;
        }

        if (!string.Equals(certificate.Subject, SynchronizationProtocol.CertificateSubjectName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        observedThumbprint = certificate.Thumbprint;
        if (string.IsNullOrWhiteSpace(observedThumbprint))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(requiredThumbprint) ||
               string.Equals(observedThumbprint, requiredThumbprint, StringComparison.OrdinalIgnoreCase);
    }
}