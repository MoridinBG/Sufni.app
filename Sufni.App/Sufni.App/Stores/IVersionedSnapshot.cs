using System.Diagnostics.CodeAnalysis;

namespace Sufni.App.Stores;

public interface IVersionedSnapshot
{
    long Updated { get; }
}

public static class VersionedSnapshotExtensions
{
    public static bool IsNewerThan([NotNullWhen(true)] this IVersionedSnapshot? current, long baselineUpdated)
        => current is not null && current.Updated > baselineUpdated;
}