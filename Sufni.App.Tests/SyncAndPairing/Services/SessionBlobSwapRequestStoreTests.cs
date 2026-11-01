using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.SyncAndPairing.Services;

public class SessionBlobSwapRequestStoreTests
{
    [Fact]
    public async Task CompletingAnIgnoredTransfer_DoesNotRemoveANewerRequest()
    {
        using var file = new TempDatabase();
        var database = new TestPersistenceHarness(file.DatabasePath);
        var session = new Session(Guid.NewGuid(), "ride", "", null);
        await database.PutSessionAsync(session);
        var connection = await database.GetInitializedConnectionAsync();
        await connection.ExecuteAsync("INSERT INTO session_blob_swap_request VALUES (?, ?, ?)",
            session.Id, "newer-target", AppJson.Serialize(new SessionProcessedGeneration(80)));

        await database.SwapRequests.CompleteRequestAsync(session.Id, "older-target");
        Assert.Equal("newer-target", (await database.SwapRequests.GetRequestAsync(session.Id))!.TargetFingerprint);

        await database.SwapRequests.CompleteRequestAsync(session.Id, "newer-target");
        Assert.Null(await database.SwapRequests.GetRequestAsync(session.Id));
    }
}
