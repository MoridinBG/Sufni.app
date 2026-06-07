using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Sufni.App.ExtensionHost;

internal sealed class AppExtensionCollection
{
    private readonly List<IAppExtensionModule> modules = [];
    private readonly HashSet<string> moduleIds = new(StringComparer.Ordinal);

    public IReadOnlyList<IAppExtensionModule> Modules => modules;

    public void Add(IAppExtensionModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        if (!moduleIds.Add(module.Id))
        {
            throw new InvalidOperationException($"An extension module with id '{module.Id}' is already registered.");
        }

        modules.Add(module);
    }

    public void RegisterServices(IServiceCollection services, AppExtensionServiceRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var module in modules)
        {
            module.RegisterServices(services, context);
        }
    }

    public void RegisterCapabilities(AppExtensionCapabilityRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        foreach (var module in modules)
        {
            module.RegisterCapabilities(registry);
        }
    }
}
