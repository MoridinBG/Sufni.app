using Sufni.App.AppleShared;

namespace Sufni.App.macOS;

public sealed class BonjourServiceDiscovery : AppleBonjourServiceDiscoveryBase
{
    public BonjourServiceDiscovery()
        : base("macOS")
    {
    }
}