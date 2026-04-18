using System.Diagnostics.CodeAnalysis;

namespace Sufni.App.Stores;

public interface IVersionedSnapshot
{
    long Updated { get; }
}

public static class VersionedSnapshotExtensions
{
    extension([NotNullWhen(true)] IVersionedSnapshot? current)
    {
        public bool IsNewerThan(long baselineUpdated)
            => current is not null && current.Updated > baselineUpdated;
    }
}