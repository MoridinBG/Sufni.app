using System;
using System.Collections.Generic;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace Sufni.App.Shell.DesktopViews;

internal sealed class InertWindowAutomationPeer(Window owner) : WindowAutomationPeer(owner)
{
    protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() => [];

    // Hiding IRootProvider makes the native bridge's IsRootProvider() checks fail,
    // which blocks the focus-change and hit-test materialization paths that ignore
    // child containment.
    protected override object? GetProviderCore(Type providerType) =>
        providerType == typeof(IRootProvider) ? null : base.GetProviderCore(providerType);
}
