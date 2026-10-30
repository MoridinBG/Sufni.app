using System;
using System.Threading.Tasks;
using System.Threading;
using Serilog;
using Sufni.App.Shell.Coordinators;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Store;
namespace Sufni.App.Sessions.Services;

internal sealed class RecordedSessionSourceRetentionCleanup(
    SqliteConnectionContext connectionContext,
    IRecordedSessionSourceRepository recordedSessionSourceRepository,
    IRecordedSessionSourceStoreWriter recordedSessionSourceStore,
    IBackgroundTaskRunner backgroundTaskRunner) : IStartupJob
{
    private static readonly ILogger logger = Log.ForContext<RecordedSessionSourceRetentionCleanup>();

    public Task RunAsync(CancellationToken cancellationToken = default) =>
        backgroundTaskRunner.RunAsync(() => RunCoreAsync(cancellationToken), cancellationToken);

    private async Task RunCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = await connectionContext.GetInitializedConnectionAsync(cancellationToken);
            var deletedIds = await recordedSessionSourceRepository.DeleteOrphanedRecordedSessionSourcesAsync();

            if (deletedIds.Count > 0)
            {
                logger.Information(
                    "Recorded-source retention cleanup removed {Count} orphaned source row(s)",
                    deletedIds.Count);
                await recordedSessionSourceStore.PublishSourcesRemovedAsync(deletedIds);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Recorded-source retention cleanup failed");
        }
    }
}
