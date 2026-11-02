using Sufni.App.Infrastructure;

using Sufni.App.AppleShared;
namespace Sufni.App.macOS;

public sealed class BonjourServiceDiscovery : AppleBonjourServiceDiscoveryBase
{
    public BonjourServiceDiscovery(ServiceDiscoveryResolution resolution)
        : base("macOS", resolution)
    {
    }
}