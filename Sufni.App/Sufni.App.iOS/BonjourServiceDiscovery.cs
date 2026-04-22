using Sufni.App.AppleShared;

namespace Sufni.App.iOS;

public sealed class BonjourServiceDiscovery : AppleBonjourServiceDiscoveryBase
{
    public BonjourServiceDiscovery()
        : base("iOS")
    {
    }
}