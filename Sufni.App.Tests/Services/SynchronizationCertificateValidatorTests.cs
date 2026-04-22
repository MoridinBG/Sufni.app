using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Sufni.App.Services;

namespace Sufni.App.Tests.Services;

public class SynchronizationCertificateValidatorTests
{
    [Fact]
    public void TryValidate_ReturnsTrue_WhenCertificateMatchesSubjectAndValidityWindow()
    {
        using var certificate = CreateCertificate(
            SynchronizationProtocol.CertificateSubjectName,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));

        var isValid = SynchronizationCertificateValidator.TryValidate(
            certificate,
            requiredThumbprint: null,
            DateTimeOffset.UtcNow,
            out var observedThumbprint);

        Assert.True(isValid);
        Assert.Equal(certificate.Thumbprint, observedThumbprint);
    }

    [Fact]
    public void TryValidate_ReturnsFalse_WhenCertificateSubjectDoesNotMatch()
    {
        using var certificate = CreateCertificate(
            "CN=wrong-subject",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));

        var isValid = SynchronizationCertificateValidator.TryValidate(
            certificate,
            requiredThumbprint: null,
            DateTimeOffset.UtcNow,
            out _);

        Assert.False(isValid);
    }

    [Fact]
    public void TryValidate_ReturnsFalse_WhenCertificateIsExpired()
    {
        using var certificate = CreateCertificate(
            SynchronizationProtocol.CertificateSubjectName,
            DateTimeOffset.UtcNow.AddDays(-2),
            DateTimeOffset.UtcNow.AddDays(-1));

        var isValid = SynchronizationCertificateValidator.TryValidate(
            certificate,
            requiredThumbprint: null,
            DateTimeOffset.UtcNow,
            out _);

        Assert.False(isValid);
    }

    [Fact]
    public void TryValidate_ReturnsTrue_WhenRequiredThumbprintMatches()
    {
        using var certificate = CreateCertificate(
            SynchronizationProtocol.CertificateSubjectName,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));

        var isValid = SynchronizationCertificateValidator.TryValidate(
            certificate,
            certificate.Thumbprint,
            DateTimeOffset.UtcNow,
            out _);

        Assert.True(isValid);
    }

    [Fact]
    public void TryValidate_ReturnsFalse_WhenRequiredThumbprintDoesNotMatch()
    {
        using var certificate = CreateCertificate(
            SynchronizationProtocol.CertificateSubjectName,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));

        var isValid = SynchronizationCertificateValidator.TryValidate(
            certificate,
            "different-thumbprint",
            DateTimeOffset.UtcNow,
            out _);

        Assert.False(isValid);
    }

    private static X509Certificate2 CreateCertificate(
        string subject,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, ecdsa, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(notBefore, notAfter);
    }
}