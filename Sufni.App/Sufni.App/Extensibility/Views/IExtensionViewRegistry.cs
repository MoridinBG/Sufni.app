using System;
using Avalonia.Controls;
using Sufni.App.Infrastructure;

namespace Sufni.App.Extensibility.Views;

internal interface IExtensionViewRegistry
{
    void Register(
        Type viewModelType,
        Func<Control> sharedFactory,
        Func<Control>? compactFactory,
        Func<Control>? workspaceFactory);
    void Register(
        Type viewModelType,
        Func<IServiceProvider, Control> sharedFactory,
        Func<IServiceProvider, Control>? compactFactory,
        Func<IServiceProvider, Control>? workspaceFactory);
    bool TryBuild(object data, UiLayoutProfile layoutProfile, out Control control);
    bool TryBuild(object data, UiLayoutProfile layoutProfile, IServiceProvider serviceProvider, out Control control);
    bool Matches(Type viewModelType, UiLayoutProfile layoutProfile);
}
