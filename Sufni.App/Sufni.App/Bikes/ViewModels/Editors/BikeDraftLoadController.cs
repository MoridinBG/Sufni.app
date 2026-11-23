using System;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Queries;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Models;

namespace Sufni.App.Bikes.ViewModels.Editors;

internal enum BikeDraftLoadKind { Open, Reset, ConflictReload }

internal sealed class BikeDraftLoadController(BikeEditorStateController controller, IBikeDraftLoader loader,
    BikeBitmapHandoff handoff, Action<FailurePresentation> error, Action<string> notify, Action<string> acceptName) : IDisposable
{
    private readonly CancellableOperation operation = new();
    public Task Pending { get; private set; } = Task.CompletedTask;

    public Task Load(BikeDraft draft, BikeDraftLoadKind kind, bool readStored = false) =>
        Pending = LoadAsync(draft, kind, readStored);

    private async Task LoadAsync(BikeDraft draft, BikeDraftLoadKind kind, bool readStored = false)
    {
        var token = operation.Start();
        var previous = controller.Current;
        var replacing = kind != BikeDraftLoadKind.Open;
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
                        controller.Actions.LoadFailed(replacing);
                        notify("The bike is no longer available.");
                        return;
                    case BikeDraftLoadResult.Failed failed:
                        controller.Actions.LoadFailed(replacing);
                        error(FailurePresentation.From(failed.Reason, failed.Details));
                        return;
                }
            }
            bool Prepare(BikeEditorImage? image)
            {
                if (!controller.Actions.Prepared(draft, image, kind != BikeDraftLoadKind.Reset,
                        kind == BikeDraftLoadKind.ConflictReload || previous.IsPersisted, busy: replacing)) return false;
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
            controller.Actions.LoadFailed(replacing, replacing ? null : draft);
            error(FailurePresentation.From(BikeImageFailureReason.Decode));
        }
    }

    public void Dispose() => operation.Dispose();
}
