using System;
using Avalonia.Controls;
using Sufni.App.ExtensionHosting;

namespace Sufni.App.ExtensionHosting;

internal interface IExtensionViewRegistry
{
    void Register(Type viewModelType, Func<Control> sharedFactory, Func<Control>? desktopFactory);
    void Register(
        Type viewModelType,
        Func<IServiceProvider, Control> sharedFactory,
        Func<IServiceProvider, Control>? desktopFactory);
    bool TryBuild(object data, bool isDesktop, out Control control);
    bool TryBuild(object data, bool isDesktop, IServiceProvider serviceProvider, out Control control);
    bool Matches(Type viewModelType, bool isDesktop);
}
