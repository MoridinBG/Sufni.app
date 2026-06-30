using System;
using Avalonia.Controls;

namespace Sufni.App.Extensibility.Views;

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
