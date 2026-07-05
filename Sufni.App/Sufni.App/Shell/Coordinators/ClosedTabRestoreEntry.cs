using System;

using Sufni.App.Shared.Base;

namespace Sufni.App.Shell.Coordinators;

public sealed class ClosedTabRestoreEntry
{
    private readonly Func<TabPageViewModelBase?> restore;

    public ClosedTabRestoreEntry(
        Type tabType,
        object key,
        Func<TabPageViewModelBase?> restore)
    {
        ArgumentNullException.ThrowIfNull(tabType);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(restore);

        TabType = tabType;
        Key = key;
        this.restore = restore;
    }

    public Type TabType { get; }

    public object Key { get; }

    public static ClosedTabRestoreEntry For<T>(
        object key,
        Func<T?> restore)
        where T : TabPageViewModelBase
    {
        ArgumentNullException.ThrowIfNull(restore);

        return new ClosedTabRestoreEntry(typeof(T), key, () => restore());
    }

    public bool Matches(Type tabType, object key) =>
        TabType == tabType && Equals(Key, key);

    public TabPageViewModelBase? Restore() => restore();
}
