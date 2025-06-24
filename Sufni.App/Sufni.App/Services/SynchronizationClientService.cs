using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Models;

namespace Sufni.App.Services;

public class SynchronizationClientService : ISynchronizationClientService
{
    private readonly IDatabaseService? databaseService = App.Current?.Services?.GetService<IDatabaseService>();
    private readonly IHttpApiService? httpApiService = App.Current?.Services?.GetService<IHttpApiService>();

    private async Task PushLocalChanges(int lastSyncTime)
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");
        Debug.Assert(httpApiService != null, nameof(httpApiService) + " != null");

        var changes = new SynchronizationData
        {
            Boards = await databaseService.GetChangedBoardsAsync(lastSyncTime),
            Setups = await databaseService.GetChangedSetupsAsync(lastSyncTime),
            Sessions = await databaseService.GetChangedSessionsAsync(lastSyncTime)
        };
        await httpApiService.PushSyncAsync(changes);
    }

    private async Task PushIncompleteSessions()
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");
        Debug.Assert(httpApiService != null, nameof(httpApiService) + " != null");

        var incompleteSessions = await httpApiService.GetIncompleteSessionIdsAsync();
        foreach (var id in incompleteSessions)
        {
            var psst = await databaseService.GetSessionRawPsstAsync(id);
            if (psst is not null)
            {
                await httpApiService.PatchSessionPsstAsync(id, psst);
            }
        }
    }

    private async Task PullRemoteChanges(int lastSyncTime)
    {
        Debug.Assert(httpApiService != null, nameof(httpApiService) + " != null");
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        var syncData = await httpApiService.PullSyncAsync(lastSyncTime);
        foreach (var board in syncData.Boards)
        {
            if (board.Deleted.HasValue)
            {
                await databaseService.DeleteBoardAsync(board.Id);
            }
            else
            {
                await databaseService.PutBoardAsync(board);
            }
        }

        foreach (var setup in syncData.Setups)
        {
            if (setup.Deleted.HasValue)
            {
                await databaseService.DeleteSetupAsync(setup.Id);
            }
            else
            {
                await databaseService.PutSetupAsync(setup);
            }
        }

        foreach (var session in syncData.Sessions)
        {
            if (session.Deleted.HasValue)
            {
                await databaseService.DeleteSessionAsync(session.Id);
            }
            else
            {
                await databaseService.PutSessionAsync(session);
            }
        }
    }

    private async Task PullIncompleteSessions()
    {
        Debug.Assert(httpApiService != null, nameof(httpApiService) + " != null");
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        var incompleteSessionIds = await databaseService.GetIncompleteSessionIdsAsync();
        foreach (var id in incompleteSessionIds)
        {
            var psst = await httpApiService.GetSessionPsstAsync(id);
            if (psst is not null)
            {
                await databaseService.PatchSessionPsstAsync(id, psst);
            }
        }
    }

    public async Task SyncAll()
    {
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        var lastSyncTime = await databaseService.GetLastSyncTimeAsync();

        await PushLocalChanges(lastSyncTime);
        await PullRemoteChanges(lastSyncTime);
        await PushIncompleteSessions();
        await PullIncompleteSessions();

        await databaseService.UpdateLastSyncTimeAsync();
    }
}
