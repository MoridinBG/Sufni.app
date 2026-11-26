using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Services;
using Sufni.Telemetry;

namespace Sufni.App.Tests.TestSupport.Persistence;

/// <summary>
/// Accepts every profile commit; key-guarded profile commits are covered by the SQLite repository tests. A class commit
/// or label commit accepts the sessions whose stored profile has the profile key of its key.
/// </summary>
internal sealed class InMemoryTerrainProfileRepository : ITerrainProfileRepository
{
    private readonly Dictionary<Guid, (TerrainProfileKey Key, TerrainMaterialization Result)> rows = [];
    public Exception? WriteFailure { get; set; }
    public Exception? ReadFailure { get; set; }
    public Func<Task>? BeforeWrite { get; set; }
    public Func<Task>? BeforeClassWrite { get; set; }
    public Func<CancellationToken, Task>? BeforeLabelWrite { get; set; }
    public Exception? LabelWriteFailure { get; set; }
    public List<IReadOnlyDictionary<Guid, (TerrainLabelKey Key, TerrainLabelSummary Summary)>> LabelWrites { get; } = [];
    public Func<IReadOnlyDictionary<Guid, TerrainProfileKey>, CancellationToken, Task>? BeforeRead { get; set; }

    public void Seed(Guid sessionId, TerrainProfileKey key, TerrainProfile profile) =>
        rows[sessionId] = (key, TerrainMaterialization.Computed(profile));

    public TerrainProfileKey? StoredKey(Guid sessionId) => rows.TryGetValue(sessionId, out var row) ? row.Key : null;

    public async Task<bool> TryCommitAsync(Guid sessionId, TerrainProfileKey expectedKey, TerrainMaterialization result,
        CancellationToken cancellationToken = default)
    {
        if (BeforeWrite is { } beforeWrite) await beforeWrite();
        cancellationToken.ThrowIfCancellationRequested();
        if (WriteFailure is { } failure) throw failure;
        rows[sessionId] = (expectedKey, result);
        return true;
    }

    public async Task<IReadOnlyDictionary<Guid, TerrainProfile>> ReadAsync(
        IReadOnlyDictionary<Guid, TerrainProfileKey> expected, CancellationToken cancellationToken = default)
    {
        if (BeforeRead is { } beforeRead) await beforeRead(expected, cancellationToken);
        if (ReadFailure is { } failure) throw failure;
        return expected.Where(pair => rows.TryGetValue(pair.Key, out var row) && row.Key == pair.Value && row.Result.Profile is not null)
            .ToDictionary(pair => pair.Key, pair => rows[pair.Key].Result.Profile!);
    }

    public async Task<IReadOnlyList<Guid>> CommitClassesAsync(
        IReadOnlyDictionary<Guid, (TerrainClassKey Key, TerrainClassSummary Summary)> written,
        CancellationToken cancellationToken = default)
    {
        if (BeforeClassWrite is { } beforeClassWrite) await beforeClassWrite();
        cancellationToken.ThrowIfCancellationRequested();
        return [.. written.Keys.Where(id =>
            rows.TryGetValue(id, out var row) && row.Key == written[id].Key.Profile && row.Result.Profile is not null)];
    }

    public async Task<IReadOnlyList<Guid>> CommitLabelsAsync(
        IReadOnlyDictionary<Guid, (TerrainLabelKey Key, TerrainLabelSummary Summary)> written,
        CancellationToken cancellationToken = default)
    {
        if (BeforeLabelWrite is { } beforeLabelWrite) await beforeLabelWrite(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (LabelWriteFailure is { } failure) throw failure;
        LabelWrites.Add(written);
        return [.. written.Keys.Where(id =>
            rows.TryGetValue(id, out var row) && row.Key == written[id].Key.Profile && row.Result.Profile is not null)];
    }
}
