using NSubstitute;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Queries;
using Sufni.App.Shared.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.Bikes.Queries;

public class BikeDraftLoaderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAndDeletedBikesAreUnavailable(bool deleted)
    {
        await using var database = new TestPersistenceHarness($"bike-{Guid.NewGuid():N}", inMemory: true);
        var id = Guid.NewGuid();
        if (deleted)
            await (await database.GetInitializedConnectionAsync()).InsertAsync(new Bike(id, "bike") { Deleted = 1 });

        IBikeDraftLoader loader = new BikeDraftLoader(database.GetRepository<Bike>(), new InlineBackgroundTaskRunner());
        Assert.IsType<BikeDraftLoadResult.Missing>(await loader.LoadDraftAsync(id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadingABikeIncludesItsStoredImage()
    {
        var row = new Bike(Guid.NewGuid(), "bike") { ImageBytes = [1, 2, 3] };
        var repository = Substitute.For<ISynchronizableRepository<Bike>>();
        repository.GetAsync(row.Id).Returns(row);
        IBikeDraftLoader loader = new BikeDraftLoader(repository, new InlineBackgroundTaskRunner());

        var loaded = Assert.IsType<BikeDraftLoadResult.Loaded>(await loader.LoadDraftAsync(row.Id, TestContext.Current.CancellationToken));
        Assert.Equal(row.ImageBytes, loaded.Draft.ImageBytes);
    }

    [Fact]
    public async Task ReadFailureIsReportedSeparatelyFromMissing()
    {
        var repository = Substitute.For<ISynchronizableRepository<Bike>>();
        repository.GetAsync(Arg.Any<Guid>()).Returns(Task.FromException<Bike?>(new IOException()));
        IBikeDraftLoader loader = new BikeDraftLoader(repository, new InlineBackgroundTaskRunner());

        var failure = Assert.IsType<BikeDraftLoadResult.Failed>(await loader.LoadDraftAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
        Assert.Equal(BikeLoadFailureReason.Read, failure.Reason);
    }
}
