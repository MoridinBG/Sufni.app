using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace Sufni.App.ExtensionHost;

public interface IAppExtensionCapabilityRegistry
{
    IReadOnlyList<Type> EagerServiceTypes { get; }
    void RegisterEagerService(Type serviceType);
    void RegisterView(Type viewModelType, Func<Control> sharedFactory, Func<Control>? desktopFactory = null);
}

public sealed class AppExtensionCapabilityRegistry : IAppExtensionCapabilityRegistry
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
}
