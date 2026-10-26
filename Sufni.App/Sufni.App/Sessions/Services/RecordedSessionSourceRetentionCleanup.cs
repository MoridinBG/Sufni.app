using System;
using System.Threading.Tasks;
using Serilog;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Store;
namespace Sufni.App.Sessions.Services;

internal sealed class RecordedSessionSourceRetentionCleanup(
    SqliteConnectionContext connectionContext,
    IRecordedSessionSourceRepository recordedSessionSourceRepository,
    IRecordedSessionSourceStoreWriter recordedSessionSourceStore,
    IBackgroundTaskRunner backgroundTaskRunner)
{
    private static readonly ILogger logger = Log.ForContext<RecordedSessionSourceRetentionCleanup>();

    public Task RunAsync() => backgroundTaskRunner.RunAsync(RunCoreAsync);

    private async Task RunCoreAsync()
    {
        try
        {
            _ = await connectionContext.GetInitializedConnectionAsync();
            var deletedIds = await recordedSessionSourceRepository.DeleteOrphanedRecordedSessionSourcesAsync();

            if (deletedIds.Count > 0)
            {
                logger.Information(
                    "Recorded-source retention cleanup removed {Count} orphaned source row(s)",
                    deletedIds.Count);
                await recordedSessionSourceStore.PublishSourcesRemovedAsync(deletedIds);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Recorded-source retention cleanup failed");
        }
    }
}
