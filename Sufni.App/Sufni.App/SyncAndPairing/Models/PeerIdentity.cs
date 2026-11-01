namespace Sufni.App.SyncAndPairing.Models;

public sealed record PeerIdentity(string CertificateThumbprint, string DatabaseIncarnation);
