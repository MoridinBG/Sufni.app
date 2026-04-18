using Sufni.App.AppleShared;

namespace Sufni.App.iOS;

public sealed class IosSecureStorage : AppleSecureStorageBase
{
    public IosSecureStorage()
        : base(platformName: "iOS", alias: "sufni.bridge.ios.preferences")
    {
    }
}