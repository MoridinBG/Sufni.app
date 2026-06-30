using DynamicData;
using NSubstitute;
using Sufni.App.Services;

using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
namespace Sufni.App.Tests.Stores;

public class RecordedSessionSourceStoreTests
{
    private readonly IRecordedSessionSourceRepository sourceRepository = Substitute.For<IRecordedSessionSourceRepository>();

    [Fact]
    public void Upsert_PublishesMetadataSnapshot()
    {
        var store = new RecordedSessionSourceStore(sourceRepository);
        using var subscription = store.Connect().Bind(out var snapshots).Subscribe();
        var source = CreateSource();

        store.Upsert(RecordedSessionSourceSnapshot.From(source));

        _ = sourceRepository.DidNotReceiveWithAnyArgs().PutRecordedSessionSourceAsync(default!);
        var snapshot = Assert.Single(snapshots);
        Assert.Equal(source.SessionId, snapshot.SessionId);
        Assert.Equal(source.SourceKind, snapshot.SourceKind);
        Assert.Equal(source.SourceName, snapshot.SourceName);
        Assert.Equal(source.SchemaVersion, snapshot.SchemaVersion);
        Assert.Equal(source.SourceHash, snapshot.SourceHash);
        Assert.Equal(snapshot, store.Get(source.SessionId));
    }

    [Fact]
    public async Task RefreshAsync_ReplacesCachedMetadataFromDatabase()
    {
        var store = new RecordedSessionSourceStore(sourceRepository);
        using var subscription = store.Connect().Bind(out var snapshots).Subscribe();
        var removed = CreateSource(name: "removed.SST");
        var kept = CreateSource(name: "kept.SST");

        store.Upsert(RecordedSessionSourceSnapshot.From(removed));
        sourceRepository.GetRecordedSessionSourcesAsync().Returns([kept]);

        await store.RefreshAsync();

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(kept.SessionId, snapshot.SessionId);
        Assert.Null(store.Get(removed.SessionId));
        Assert.Equal(kept.SourceHash, store.Get(kept.SessionId)!.SourceHash);
    }

    [Fact]
    public async Task LoadAsync_ReturnsRawSourceFromDatabase()
    {
        var store = new RecordedSessionSourceStore(sourceRepository);
        var source = CreateSource();
        sourceRepository.GetRecordedSessionSourceAsync(source.SessionId).Returns(source);

        var loaded = await store.LoadAsync(source.SessionId);

        Assert.Same(source, loaded);
    }

    [Fact]
    public void Remove_RemovesCachedSnapshot()
    {
        var store = new RecordedSessionSourceStore(sourceRepository);
        using var subscription = store.Connect().Bind(out var snapshots).Subscribe();
        var source = CreateSource();
        store.Upsert(RecordedSessionSourceSnapshot.From(source));

        store.Remove(source.SessionId);

        _ = sourceRepository.DidNotReceiveWithAnyArgs().DeleteRecordedSessionSourceAsync(default);
        Assert.Empty(snapshots);
        Assert.Null(store.Get(source.SessionId));
    }

    private static RecordedSessionSource CreateSource(string name = "recording.SST")
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        return new RecordedSessionSource
        {
            SessionId = Guid.NewGuid(),
            SourceKind = RecordedSessionSourceKind.ImportedSst,
            SourceName = name,
            SchemaVersion = 1,
            SourceHash = RecordedSessionSourceHash.Compute(
                RecordedSessionSourceKind.ImportedSst,
                name,
                1,
                payload),
            Payload = payload
        };
    }
}
