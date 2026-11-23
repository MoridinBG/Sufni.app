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
    public Task Pending { get; private set; } = Task.CompletedTask;

    public Task Load(BikeDraft draft, bool readStored, bool accept, bool busy, bool? persisted = null) =>
        Pending = LoadAsync(draft, readStored, accept, busy, persisted);

    private async Task LoadAsync(BikeDraft draft, bool readStored, bool accept, bool busy, bool? persisted = null)
    {
        var token = operation.Start();
        var previous = controller.Current;
        var ready = previous.Phase == BikeEditorPhase.Ready;
        if (!controller.Actions.LoadStarted()) return;
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
                        controller.Actions.LoadFailed(ready);
                        notify("The bike is no longer available.");
                        return;
                    case BikeDraftLoadResult.Failed failed:
                        controller.Actions.LoadFailed(ready);
                        error(FailurePresentation.From(failed.Reason, failed.Details));
                        return;
                }
            }
            bool Prepare(BikeEditorImage? image)
            {
                if (!controller.Actions.Prepared(draft, image, accept, persisted ?? previous.IsPersisted, busy)) return false;
                acceptName(draft.Name);
                return true;
            }

            if (draft.RearSuspension.Kind == RearSuspensionKind.Linkage && !draft.ImageBytesMemory.IsEmpty)
                await handoff.DecodeAsync(draft.ImageBytes, Prepare, token);
            else Prepare(null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (token.IsCancellationRequested) return;
            Log.ForContext<BikeDraftLoadController>().Warning(e, "Could not decode bike image for {BikeId}", draft.Id);
            controller.Actions.LoadFailed(ready, ready ? null : draft);
            error(FailurePresentation.From(BikeImageFailureReason.Decode));
        }
    }

    public void Dispose() => operation.Dispose();
}
