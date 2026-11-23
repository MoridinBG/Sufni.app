using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.Services;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Models;

namespace Sufni.App.Bikes.ViewModels.Editors;

internal sealed class BikeEditorAnalysisDemand(IRearSuspensionAnalysisQuery query, BikeEditorActions actions,
    TimeProvider clock, Action<FailurePresentation> error) : IDisposable
{
    private readonly CancellableOperation operation = new();
    private long generation;
    public Task Pending { get; private set; } = Task.CompletedTask;

    public void Apply(BikeAnalysisRequest request)
    {
        if (request.Generation == generation) return;
        generation = request.Generation;
        var token = operation.Start();
        Pending = request.Spec is null ? Task.CompletedTask : ComputeAsync(request, token);
    }

    private async Task ComputeAsync(BikeAnalysisRequest request, CancellationToken token)
    {
        BikeEditorAnalysisResult result;
        try
        {
            if (request.Debounce) await Task.Delay(TimeSpan.FromMilliseconds(200), clock, token);
            token.ThrowIfCancellationRequested();
            result = await query.LoadAnalysisAsync(request.Spec!, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception exception)
        {
            if (token.IsCancellationRequested) return;
            Log.ForContext<BikeEditorAnalysisDemand>().Error(exception, "Could not analyze rear suspension");
            result = new BikeEditorAnalysisResult.Failed(BikeAnalysisFailureReason.Analyze);
        }

        // Cached queries may complete during the request's own state publication.
        await Task.Yield();
        if (!token.IsCancellationRequested && actions.AnalysisCompleted(request.Generation, result) &&
            result is BikeEditorAnalysisResult.Failed failed)
            error(FailurePresentation.From(failed.Reason, failed.Details));
    }

    public void Dispose() => operation.Dispose();
}
