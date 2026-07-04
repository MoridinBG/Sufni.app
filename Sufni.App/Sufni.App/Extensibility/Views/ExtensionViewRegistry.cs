using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Sufni.App;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.Common;
namespace Sufni.App.Extensibility.Views;

internal sealed class ExtensionViewRegistry : IExtensionViewRegistry
{
    private readonly Dictionary<Type, ExtensionViewFactories> factoriesByViewModelType = [];

    public void Register(
        Type viewModelType,
        Func<Control> sharedFactory,
        Func<Control>? compactFactory,
        Func<Control>? workspaceFactory)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        ArgumentNullException.ThrowIfNull(sharedFactory);

        Register(
            viewModelType,
            _ => sharedFactory(),
            compactFactory is null ? null : _ => compactFactory(),
            workspaceFactory is null ? null : _ => workspaceFactory());
    }

    public void Register(
        Type viewModelType,
        Func<IServiceProvider, Control> sharedFactory,
        Func<IServiceProvider, Control>? compactFactory,
        Func<IServiceProvider, Control>? workspaceFactory)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        ArgumentNullException.ThrowIfNull(sharedFactory);

        if (factoriesByViewModelType.ContainsKey(viewModelType))
        {
            throw new InvalidOperationException(
                $"An extension view is already registered for view-model type '{viewModelType.FullName}'.");
        }

        factoriesByViewModelType[viewModelType] = new ExtensionViewFactories(
            sharedFactory,
            compactFactory,
            workspaceFactory);
    }

    public bool TryBuild(object data, UiLayoutProfile layoutProfile, out Control control)
    {
        return TryBuild(data, layoutProfile, EmptyServiceProvider.Instance, out control);
    }

    public bool TryBuild(
        object data,
        UiLayoutProfile layoutProfile,
        IServiceProvider serviceProvider,
        out Control control)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var viewModelType = data.GetType();
        if (!factoriesByViewModelType.TryGetValue(viewModelType, out var factories))
        {
            control = null!;
            return false;
        }

        control = layoutProfile switch
        {
            UiLayoutProfile.Compact when factories.CompactFactory is not null =>
                factories.CompactFactory(serviceProvider),
            UiLayoutProfile.Workspace when factories.WorkspaceFactory is not null =>
                factories.WorkspaceFactory(serviceProvider),
            _ => factories.SharedFactory(serviceProvider),
        };
        return true;
    }

    public bool Matches(Type viewModelType, UiLayoutProfile layoutProfile)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);

        return factoriesByViewModelType.ContainsKey(viewModelType);
    }

    private sealed record ExtensionViewFactories(
        Func<IServiceProvider, Control> SharedFactory,
        Func<IServiceProvider, Control>? CompactFactory,
        Func<IServiceProvider, Control>? WorkspaceFactory);

}
