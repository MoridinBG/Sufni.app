using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Sufni.App.Bikes.Services;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Models;

namespace Sufni.App.Bikes.ViewModels.Editors;

internal sealed partial class BikeImageOpenController(BikeEditorStateController controller, IBikeEditorService files,
    BikeBitmapHandoff handoff, Action<FailurePresentation> error, Action<string> restoreName) : IDisposable
{
    private readonly CancellableOperation operation = new();

    private bool CanOpenImage() => controller.Current.Phase == BikeEditorPhase.Ready ||
        (controller.Current.Phase == BikeEditorPhase.Unavailable && !controller.Current.Baseline.ImageBytesMemory.IsEmpty);

    [RelayCommand(CanExecute = nameof(CanOpenImage))]
    private async Task OpenImage()
    {
        var token = operation.Start();
        var previous = controller.Current;
        try
        {
            var result = await files.LoadImageAsync(token);
            token.ThrowIfCancellationRequested();
            if (result is BikeImageLoadResult.Failed failed) error(FailurePresentation.From(failed.Reason, failed.Details));
            if (result is BikeImageLoadResult.Loaded loaded)
                await handoff.DecodeAsync(loaded.ImageBytes, async image =>
                {
                    if (previous.Phase != BikeEditorPhase.Unavailable) return await controller.Actions.ApplyImage(image);
                    if (!await controller.Actions.Prepared(previous.Baseline, image, BikeDraftLoadKind.ImageRecovery)) return false;
                    restoreName(previous.Baseline.Name);
                    return true;
                }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (token.IsCancellationRequested) return;
            Log.ForContext<BikeImageOpenController>().Warning(e, "Selected bike image could not be decoded");
            error(FailurePresentation.From(BikeImageFailureReason.Decode));
        }
    }

    public void Cancel() => operation.Cancel();
    public void Dispose() => operation.Dispose();
}
