using Sufni.App.AppleShared;

namespace Sufni.App.macOS;

public sealed class MacOsSecureStorage : AppleSecureStorageBase
{
    public MacOsSecureStorage()
        : base(platformName: "macOS", alias: "sufni.bridge.macos.preferences")
    {
    }
}