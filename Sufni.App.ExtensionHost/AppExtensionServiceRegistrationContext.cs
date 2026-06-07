namespace Sufni.App.ExtensionHost;

public sealed class AppExtensionServiceRegistrationContext
{
    public AppExtensionServiceRegistrationContext(bool isDesktop)
    {
        IsDesktop = isDesktop;
    }

    public bool IsDesktop { get; }
}

