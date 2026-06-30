namespace Sufni.App.ExtensionHost.Contracts.Capabilities;

public sealed class AppExtensionServiceRegistrationContext
{
    public AppExtensionServiceRegistrationContext(bool isDesktop)
    {
        IsDesktop = isDesktop;
    }

    public bool IsDesktop { get; }
}

