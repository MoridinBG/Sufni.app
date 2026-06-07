using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace Sufni.App.ExtensionHost;

public interface IAppExtensionCapabilityRegistry
{
    IReadOnlyList<Type> EagerServiceTypes { get; }
    void RegisterEagerService(Type serviceType);
    void RegisterView(Type viewModelType, Func<Control> sharedFactory, Func<Control>? desktopFactory = null);
    void RegisterView(
        Type viewModelType,
        Func<IServiceProvider, Control> sharedFactory,
        Func<IServiceProvider, Control>? desktopFactory = null);
}
