using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Sufni.App;

using Sufni.App.Shared.Common;
namespace Sufni.App.Extensibility.Views;

internal sealed class ExtensionViewRegistry : IExtensionViewRegistry
{
    private readonly Dictionary<Type, ExtensionViewFactories> factoriesByViewModelType = [];

    public void Register(Type viewModelType, Func<Control> sharedFactory, Func<Control>? desktopFactory)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        ArgumentNullException.ThrowIfNull(sharedFactory);

        Register(
            viewModelType,
            _ => sharedFactory(),
            desktopFactory is null ? null : _ => desktopFactory());
    }

    public void Register(
        Type viewModelType,
        Func<IServiceProvider, Control> sharedFactory,
        Func<IServiceProvider, Control>? desktopFactory)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        ArgumentNullException.ThrowIfNull(sharedFactory);

        if (factoriesByViewModelType.ContainsKey(viewModelType))
        {
            throw new InvalidOperationException(
                $"An extension view is already registered for view-model type '{viewModelType.FullName}'.");
        }

        factoriesByViewModelType[viewModelType] = new ExtensionViewFactories(sharedFactory, desktopFactory);
    }

    public bool TryBuild(object data, bool isDesktop, out Control control)
    {
        return TryBuild(data, isDesktop, EmptyServiceProvider.Instance, out control);
    }

    public bool TryBuild(object data, bool isDesktop, IServiceProvider serviceProvider, out Control control)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var viewModelType = data.GetType();
        if (!factoriesByViewModelType.TryGetValue(viewModelType, out var factories))
        {
            control = null!;
            return false;
        }

        control = isDesktop && factories.DesktopFactory is not null
            ? factories.DesktopFactory(serviceProvider)
            : factories.SharedFactory(serviceProvider);
        return true;
    }

    public bool Matches(Type viewModelType, bool isDesktop)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);

        return factoriesByViewModelType.ContainsKey(viewModelType);
    }

    private sealed record ExtensionViewFactories(
        Func<IServiceProvider, Control> SharedFactory,
        Func<IServiceProvider, Control>? DesktopFactory);

}
