using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Sufni.App.ExtensionHosting;
using Sufni.App.ExtensionHost.Contracts;

namespace Sufni.App.ExtensionHosting;

internal sealed class AppExtensionCapabilityRegistry : IAppExtensionCapabilityRegistry
{
    private readonly IExtensionViewRegistry viewRegistry;
    private readonly List<Type> eagerServiceTypes = [];
    private readonly HashSet<Type> eagerServiceTypeSet = [];

    public AppExtensionCapabilityRegistry(IExtensionViewRegistry viewRegistry)
    {
        this.viewRegistry = viewRegistry;
    }

    public IReadOnlyList<Type> EagerServiceTypes => eagerServiceTypes;

    public void RegisterEagerService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (eagerServiceTypeSet.Add(serviceType))
        {
            eagerServiceTypes.Add(serviceType);
        }
    }

    public void RegisterEagerService<TService>()
        where TService : class
    {
        RegisterEagerService(typeof(TService));
    }

    public void RegisterView(Type viewModelType, Func<Control> sharedFactory, Func<Control>? desktopFactory = null)
    {
        viewRegistry.Register(viewModelType, sharedFactory, desktopFactory);
    }

    public void RegisterView(
        Type viewModelType,
        Func<IServiceProvider, Control> sharedFactory,
        Func<IServiceProvider, Control>? desktopFactory = null)
    {
        viewRegistry.Register(viewModelType, sharedFactory, desktopFactory);
    }
}
