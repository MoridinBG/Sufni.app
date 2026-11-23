using System;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Queries;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Models;

namespace Sufni.App.Bikes.ViewModels.Editors;

internal sealed class BikeDraftLoadController(BikeEditorStateController controller, IBikeDraftLoader loader,
    BikeBitmapHandoff handoff, Action<FailurePresentation> error, Action<string> notify, Action<string> acceptName) : IDisposable
{
    private readonly CancellableOperation operation = new();

    /// <summary>The latest load. Close awaits it before it releases the editor.</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    public Task Load(BikeDraft draft, BikeDraftLoadKind kind, bool readStored = false) =>
        Pending = LoadAsync(draft, kind, readStored);

    private async Task LoadAsync(BikeDraft draft, BikeDraftLoadKind kind, bool readStored)
    {
        var token = operation.Start();
        if (!await controller.Actions.LoadStarted()) return;
        try
        {
            if (readStored)
            {
                var result = await loader.LoadDraftAsync(draft.Id, token);
                token.ThrowIfCancellationRequested();
                switch (result)
                {
                    case BikeDraftLoadResult.Loaded loaded: draft = loaded.Draft; break;
                    case BikeDraftLoadResult.Missing:
                        await controller.Actions.LoadFailed(kind);
                        notify("The bike is no longer available.");
                        return;
                    case BikeDraftLoadResult.Failed failed:
                        await controller.Actions.LoadFailed(kind);
                        error(FailurePresentation.From(failed.Reason, failed.Details));
                        return;
                }
            }

            async Task<bool> Prepare(BikeEditorImage? image)
            {
                if (!await controller.Actions.Prepared(draft, image, kind)) return false;
                acceptName(draft.Name);
                return true;
            }

            if (draft.RearSuspension.Kind == RearSuspensionKind.Linkage && !draft.ImageBytesMemory.IsEmpty)
                await handoff.DecodeAsync(draft.ImageBytes, Prepare, token);
            else await Prepare(null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (token.IsCancellationRequested) return;
            Log.ForContext<BikeDraftLoadController>().Warning(e, "Could not decode bike image for {BikeId}", draft.Id);
            await controller.Actions.LoadFailed(kind, draft);
            error(FailurePresentation.From(BikeImageFailureReason.Decode));
        }
    }

    public void Dispose() => operation.Dispose();
}
