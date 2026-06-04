using System;
using Avalonia.Controls;

namespace Sufni.App.ExtensionHost;

public interface IExtensionViewRegistry
{
    void Register(Type viewModelType, Func<Control> sharedFactory, Func<Control>? desktopFactory);
    bool TryBuild(object data, bool isDesktop, out Control control);
    bool Matches(Type viewModelType, bool isDesktop);
}

