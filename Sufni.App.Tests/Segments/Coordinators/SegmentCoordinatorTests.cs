using NSubstitute;

using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.Stores;
using Sufni.App.Sessions.Store;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Segments.Coordinators;

public class SegmentCoordinatorTests
{
    private readonly ISessionSegmentRepository repository = Substitute.For<ISessionSegmentRepository>();
    private readonly ISessionSegmentStoreWriter segmentStore = Substitute.For<ISessionSegmentStoreWriter>();
    private readonly ISessionStore sessionStore = Substitute.For<ISessionStore>();
    private readonly SessionSnapshot session = TestSnapshots.Session(id: Guid.NewGuid());

    public SegmentCoordinatorTests()
    {
        sessionStore.Get(session.Id).Returns(session);
        segmentStore.GetForSession(session.Id).Returns([new SessionSegmentSnapshot(Guid.NewGuid(), session.Id, "Segment 1", 0, 1)]);
    }

    [Fact]
    public async Task CreateAsync_PublishesInsertedSegment()
    {
        var result = await CreateCoordinator().CreateAsync(session.Id, " climb ", 2, 5);

        Assert.IsType<SegmentChangeResult.Applied>(result);
        await repository.Received(1).InsertAsync(Arg.Is<SessionSegment>(segment =>
            segment.SessionId == session.Id && segment.Name == "climb" && segment.StartSeconds == 2 && segment.EndSeconds == 5));
        await segmentStore.Received(1).PublishSegmentsAsync(Arg.Is<IReadOnlyCollection<SessionSegment>>(segments =>
            segments.Single().Name == "climb"));
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateName_FailsWithoutWriting()
    {
        var result = await CreateCoordinator().CreateAsync(session.Id, "Segment 1", 2, 5);

        Assert.IsType<SegmentChangeResult.Failed>(result);
        await repository.DidNotReceive().InsertAsync(Arg.Any<SessionSegment>());
    }

    [Fact]
    public async Task DeleteAsync_WhenSegmentIsGone_Fails()
    {
        repository.DeleteAsync(Arg.Any<Guid>()).Returns((SessionSegment?)null);

        var result = await CreateCoordinator().DeleteAsync(Guid.NewGuid());

        Assert.IsType<SegmentChangeResult.Failed>(result);
        await segmentStore.DidNotReceive().PublishSegmentsAsync(Arg.Any<IReadOnlyCollection<SessionSegment>>());
    }

    [Fact]
    public void NextName_SkipsNamesInUse()
    {
        Assert.Equal("Segment 2", CreateCoordinator().NextName(session.Id));
    }

    private SegmentCoordinator CreateCoordinator() => new(repository, segmentStore, sessionStore);
}
