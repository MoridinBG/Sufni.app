using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace Sufni.App.ExtensionHost.Contracts.Capabilities;

public interface IAppExtensionCapabilityRegistry
{
    IReadOnlyList<Type> EagerServiceTypes { get; }
    void RegisterEagerService(Type serviceType);
    void RegisterView(
        Type viewModelType,
        Func<Control> sharedFactory,
        Func<Control>? compactFactory = null,
        Func<Control>? workspaceFactory = null);
    void RegisterView(
        Type viewModelType,
        Func<IServiceProvider, Control> sharedFactory,
        Func<IServiceProvider, Control>? compactFactory = null,
        Func<IServiceProvider, Control>? workspaceFactory = null);
}
