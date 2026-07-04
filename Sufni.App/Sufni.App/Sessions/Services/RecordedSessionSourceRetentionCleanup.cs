using System;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Store;
namespace Sufni.App.Sessions.Services;

internal sealed class RecordedSessionSourceRetentionCleanup(
    SqliteConnectionContext connectionContext,
    IRecordedSessionSourceRepository recordedSessionSourceRepository,
    IRecordedSessionSourceStoreWriter recordedSessionSourceStore,
    IRecordedSessionDerivationWindowProvider windowProvider,
    IBackgroundTaskRunner backgroundTaskRunner)
{
    private static readonly ILogger logger = Log.ForContext<RecordedSessionSourceRetentionCleanup>();

    public Task RunAsync() => backgroundTaskRunner.RunAsync(RunCoreAsync);

    private async Task RunCoreAsync()
    {
        try
        {
            _ = await connectionContext.GetInitializedConnectionAsync();
            var retainedSourceIds = (await windowProvider.GetReferencedSourceSessionIdsAsync()).ToHashSet();
            retainedSourceIds.UnionWith(await recordedSessionSourceRepository.GetPersistedDerivationSourceSessionIdsAsync());
            var deleted = await recordedSessionSourceRepository.DeleteOrphanedRecordedSessionSourcesAsync(retainedSourceIds);

            if (deleted > 0)
            {
                logger.Information("Recorded-source retention cleanup removed {Count} orphaned source row(s)", deleted);
                await recordedSessionSourceStore.RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Recorded-source retention cleanup failed");
        }
    }
}
