using System.Reactive.Subjects;
using DynamicData;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.Coordinators;

public class LiveDaqCoordinatorTests
{
    private readonly TestLiveDaqStore liveDaqStore = new();
    private readonly ILiveDaqKnownBoardsQuery knownBoardsQuery = Substitute.For<ILiveDaqKnownBoardsQuery>();
    private readonly ILiveDaqCatalogService catalogService = Substitute.For<ILiveDaqCatalogService>();
    private readonly ILiveDaqClientFactory clientFactory = Substitute.For<ILiveDaqClientFactory>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly BehaviorSubject<IReadOnlyList<KnownLiveDaqRecord>> knownBoardsChanges = new([]);
    private readonly BehaviorSubject<IReadOnlyList<LiveDaqCatalogEntry>> catalogEntries = new([]);
    private readonly IDisposable browseLease = Substitute.For<IDisposable>();

    public LiveDaqCoordinatorTests()
    {
        knownBoardsQuery.Changes.Returns(knownBoardsChanges);
        catalogService.Observe().Returns(catalogEntries);
        catalogService.AcquireBrowse().Returns(browseLease);
    }

    private LiveDaqCoordinator CreateCoordinator() =>
        new(liveDaqStore, knownBoardsQuery, catalogService, clientFactory, shell, dialogService);

    [Fact]
    public void Activate_SeedsOfflineKnownBoards_AndAcquiresBrowse()
    {
        var boardId = Guid.NewGuid();
        knownBoardsChanges.OnNext(
        [
            new KnownLiveDaqRecord(
                IdentityKey: boardId.ToString(),
                DisplayName: boardId.ToString(),
                BoardId: boardId.ToString(),
                SetupId: Guid.NewGuid(),
                SetupName: "park setup",
                BikeId: Guid.NewGuid(),
                BikeName: "demo bike")
        ]);

        CreateCoordinator().Activate();

        catalogService.Received(1).AcquireBrowse();
        var snapshot = Assert.Single(liveDaqStore.Items);
        Assert.Equal(boardId.ToString(), snapshot.IdentityKey);
        Assert.False(snapshot.IsOnline);
        Assert.Equal("park setup", snapshot.SetupName);
        Assert.Equal("demo bike", snapshot.BikeName);
        Assert.Null(snapshot.Endpoint);
    }

    [Fact]
    public void Activate_MergesDiscoveredEntries_AndRefreshesEnrichment_OnQueryChange()
    {
        var boardId = Guid.NewGuid();
        knownBoardsChanges.OnNext(
        [
            new KnownLiveDaqRecord(
                IdentityKey: boardId.ToString(),
                DisplayName: boardId.ToString(),
                BoardId: boardId.ToString(),
                SetupId: Guid.NewGuid(),
                SetupName: "old setup",
                BikeId: Guid.NewGuid(),
                BikeName: "old bike")
        ]);

        var coordinator = CreateCoordinator();
        coordinator.Activate();

        catalogEntries.OnNext(
        [
            new LiveDaqCatalogEntry(boardId.ToString(), boardId.ToString(), boardId.ToString(), "192.168.0.20", 5555),
            new LiveDaqCatalogEntry("192.168.0.21:6666", "192.168.0.21:6666", null, "192.168.0.21", 6666)
        ]);

        var knownSnapshot = liveDaqStore.Get(boardId.ToString());
        Assert.NotNull(knownSnapshot);
        Assert.True(knownSnapshot.IsOnline);
        Assert.Equal("192.168.0.20:5555", knownSnapshot.Endpoint);
        Assert.Equal("old setup", knownSnapshot.SetupName);
        Assert.Equal("old bike", knownSnapshot.BikeName);

        var discoveredOnly = liveDaqStore.Get("192.168.0.21:6666");
        Assert.NotNull(discoveredOnly);
        Assert.True(discoveredOnly.IsOnline);
        Assert.Null(discoveredOnly.SetupName);
        Assert.Null(discoveredOnly.BikeName);

        knownBoardsChanges.OnNext(
        [
            new KnownLiveDaqRecord(
                IdentityKey: boardId.ToString(),
                DisplayName: boardId.ToString(),
                BoardId: boardId.ToString(),
                SetupId: Guid.NewGuid(),
                SetupName: "new setup",
                BikeId: Guid.NewGuid(),
                BikeName: "new bike")
        ]);

        knownSnapshot = liveDaqStore.Get(boardId.ToString());
        Assert.NotNull(knownSnapshot);
        Assert.True(knownSnapshot.IsOnline);
        Assert.Equal("new setup", knownSnapshot.SetupName);
        Assert.Equal("new bike", knownSnapshot.BikeName);
        Assert.Equal("192.168.0.20:5555", knownSnapshot.Endpoint);
    }

    [Fact]
    public void Deactivate_DisposesBrowseLease_AndLeavesOnlyOfflineKnownBoards()
    {
        var boardId = Guid.NewGuid();
        knownBoardsChanges.OnNext(
        [
            new KnownLiveDaqRecord(
                IdentityKey: boardId.ToString(),
                DisplayName: boardId.ToString(),
                BoardId: boardId.ToString(),
                SetupId: null,
                SetupName: null,
                BikeId: null,
                BikeName: null)
        ]);

        var coordinator = CreateCoordinator();
        coordinator.Activate();
        catalogEntries.OnNext(
        [
            new LiveDaqCatalogEntry(boardId.ToString(), boardId.ToString(), boardId.ToString(), "192.168.0.20", 5555),
            new LiveDaqCatalogEntry("192.168.0.21:6666", "192.168.0.21:6666", null, "192.168.0.21", 6666)
        ]);

        coordinator.Deactivate();

        browseLease.Received(1).Dispose();
        var knownSnapshot = liveDaqStore.Get(boardId.ToString());
        Assert.NotNull(knownSnapshot);
        Assert.False(knownSnapshot.IsOnline);
        Assert.Null(knownSnapshot.Endpoint);
        Assert.Null(liveDaqStore.Get("192.168.0.21:6666"));
        Assert.Single(liveDaqStore.Items);
    }

    [Fact]
    public async Task SelectAsync_RoutesThroughOpenOrFocus_WithIdentityMatcher()
    {
        var snapshot = new LiveDaqSnapshot(
            IdentityKey: "board-1",
            DisplayName: "Board 1",
            BoardId: "board-1",
            Host: "192.168.0.30",
            Port: 7777,
            IsOnline: true,
            SetupName: "setup",
            BikeName: "bike");
        liveDaqStore.Upsert(snapshot);

        Func<LiveDaqDetailViewModel, bool>? capturedMatch = null;
        Func<LiveDaqDetailViewModel>? capturedCreate = null;
        shell.When(s => s.OpenOrFocus(
                Arg.Any<Func<LiveDaqDetailViewModel, bool>>(),
                Arg.Any<Func<LiveDaqDetailViewModel>>()))
            .Do(callInfo =>
            {
                capturedMatch = callInfo.ArgAt<Func<LiveDaqDetailViewModel, bool>>(0);
                capturedCreate = callInfo.ArgAt<Func<LiveDaqDetailViewModel>>(1);
            });

        await CreateCoordinator().SelectAsync(snapshot.IdentityKey);

        shell.Received(1).OpenOrFocus(
            Arg.Any<Func<LiveDaqDetailViewModel, bool>>(),
            Arg.Any<Func<LiveDaqDetailViewModel>>());

        Assert.NotNull(capturedCreate);
        var created = capturedCreate();
        Assert.Equal(snapshot.IdentityKey, created.IdentityKey);
        Assert.Equal(snapshot.DisplayName, created.Name);
        Assert.NotNull(capturedMatch);
        Assert.True(capturedMatch(created));

        var other = new LiveDaqDetailViewModel(
            snapshot with { IdentityKey = "board-2", DisplayName = "Board 2", BoardId = "board-2" },
            clientFactory,
            shell,
            dialogService);
        Assert.False(capturedMatch(other));
    }

    private sealed class TestLiveDaqStore : ILiveDaqStoreWriter
    {
        private readonly SourceCache<LiveDaqSnapshot, string> source = new(snapshot => snapshot.IdentityKey);

        public IReadOnlyCollection<LiveDaqSnapshot> Items => source.Items;

        public IObservable<IChangeSet<LiveDaqSnapshot, string>> Connect() => source.Connect();

        public LiveDaqSnapshot? Get(string identityKey)
        {
            var lookup = source.Lookup(identityKey);
            return lookup.HasValue ? lookup.Value : null;
        }

        public void Upsert(LiveDaqSnapshot snapshot) => source.AddOrUpdate(snapshot);

        public void Remove(string identityKey) => source.RemoveKey(identityKey);

        public void Clear() => source.Clear();
    }
}