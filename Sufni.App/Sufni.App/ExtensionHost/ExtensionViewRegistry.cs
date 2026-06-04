using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace Sufni.App.ExtensionHost;

public sealed class ExtensionViewRegistry : IExtensionViewRegistry
{
    private readonly Dictionary<Type, ExtensionViewFactories> factoriesByViewModelType = [];

    public void Register(Type viewModelType, Func<Control> sharedFactory, Func<Control>? desktopFactory)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        ArgumentNullException.ThrowIfNull(sharedFactory);

        factoriesByViewModelType[viewModelType] = new ExtensionViewFactories(sharedFactory, desktopFactory);
    }

    public bool TryBuild(object data, bool isDesktop, out Control control)
    {
        ArgumentNullException.ThrowIfNull(data);

        var viewModelType = data.GetType();
        if (!factoriesByViewModelType.TryGetValue(viewModelType, out var factories))
        {
            control = null!;
            return false;
        }

        control = isDesktop && factories.DesktopFactory is not null
            ? factories.DesktopFactory()
            : factories.SharedFactory();
        return true;
    }

    public bool Matches(Type viewModelType, bool isDesktop)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);

        return factoriesByViewModelType.ContainsKey(viewModelType);
    }

    private sealed record ExtensionViewFactories(Func<Control> SharedFactory, Func<Control>? DesktopFactory);
}

