namespace Sufni.App.ExtensionHost.Contracts;

public sealed class AppExtensionServiceRegistrationContext
{
    public AppExtensionServiceRegistrationContext(bool isDesktop)
    {
        IsDesktop = isDesktop;
    }

    public bool IsDesktop { get; }
}

