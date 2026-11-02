namespace Sufni.App.SyncAndPairing.Models;

/// <summary>A discovered address, valid only while observing the peer.</summary>
public sealed record PeerEndpoint(string Address);
