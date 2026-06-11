using Microsoft.Extensions.DependencyInjection;

namespace Sufni.App.ExtensionHost.Contracts;

public interface IAppExtensionModule
{
    string Id { get; }
    void RegisterServices(IServiceCollection services, AppExtensionServiceRegistrationContext context);
    void RegisterCapabilities(IAppExtensionCapabilityRegistry registry);
}

