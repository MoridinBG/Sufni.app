using Sufni.App.Shared.Models;
using Microsoft.Extensions.Time.Testing;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Headless.XUnit;
using Sufni.App.Tests.TestSupport.Async;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.Kinematics;

using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.App.Infrastructure;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.ViewModels.LinkageEditing;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Bikes.ViewModels.Editors;

[Collection("Ui")]
public class BikeEditorAnalysisDemandTests : BikeEditorFixture
{
    [AvaloniaFact]
    public async Task Analysis_DiscardsCompletionForSupersededSpec()
    {
        var editor = CreateEditor(FullSuspensionSnapshot(), isNew: true);
        var pending = new TaskCompletionSource<BikeEditorAnalysisResult>();
        CancellationToken previousToken = default;
        analysis.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            previousToken = call.Arg<CancellationToken>();
            return pending.Task;
        });
        var initialLoad = editor.LoadedCommand.ExecuteAsync(null);
        var current = new CoordinateList([3, 4], [5, 6]);
        var computed = new TaskCompletionSource();
        analysis.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            computed.SetResult();
            return new BikeEditorAnalysisResult.Computed(PresentationData(current));
        });

        editor.Frame.ShockStroke = 25;
        Assert.True(previousToken.IsCancellationRequested);
        timeProvider.Advance(TimeSpan.FromMilliseconds(200));
        await computed.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        pending.SetResult(new BikeEditorAnalysisResult.Computed(PresentationData(new CoordinateList([0, 1], [1, 2]))));
        await initialLoad.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(current, editor.Analysis.LeverageRatioData);
        Assert.False(editor.Analysis.IsPlotBusy);
    }

    [AvaloniaFact]
    public async Task Loaded_AppliesComputedAnalysisResult()
    {
        var snapshot = FullSuspensionSnapshot(
            frontWheelDiameter: 760,
            rearWheelDiameter: 750);
        var editor = CreateEditor(snapshot, isNew: true);
        var data = new CoordinateList([1, 2], [3, 4]);
        var rearAxlePathData = new CoordinateList([2, 4], [0.25, 0.75]);
        analysis.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(
                new BikeEditorAnalysisResult.Computed(new BikeAnalysisPresentationData(data, rearAxlePathData))));

        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Equal(data, editor.Analysis.LeverageRatioData);
        Assert.Equal(2, editor.Linkage.RearAxlePath.Count);
        Assert.Equal(2, editor.Linkage.RearAxlePath[0].X, 3);
        Assert.Equal(0.75, editor.Linkage.RearAxlePath[0].Y, 3);
        Assert.Equal(4, editor.Linkage.RearAxlePath[1].X, 3);
        Assert.Equal(0.25, editor.Linkage.RearAxlePath[1].Y, 3);
    }

    [AvaloniaFact]
    public async Task Loaded_ClearsLeverageRatioData_WhenAnalysisUnavailable()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot, isNew: true);
        analysis.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(new BikeEditorAnalysisResult.Computed(PresentationData(new CoordinateList([1, 2], [3, 4]))));
        await editor.LoadedCommand.ExecuteAsync(null);
        Assert.NotNull(editor.Analysis.LeverageRatioData);
        analysis.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(new BikeEditorAnalysisResult.NotApplicable()));

        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Null(editor.Analysis.LeverageRatioData);
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Loaded_ClearsLeverageRatioData_AndAppendsError_WhenAnalysisFails()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot, isNew: true);
        analysis.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(new BikeEditorAnalysisResult.Computed(PresentationData(new CoordinateList([1, 2], [3, 4]))));
        await editor.LoadedCommand.ExecuteAsync(null);
        Assert.NotNull(editor.Analysis.LeverageRatioData);
        analysis.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(new BikeEditorAnalysisResult.Failed(BikeAnalysisFailureReason.Analyze, "boom")));

        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Null(editor.Analysis.LeverageRatioData);
        Assert.Single(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Reset_ShowsPlotBusyWhileAnalysisRefreshRuns()
    {
        var snapshot = FullSuspensionSnapshot();
        var editor = CreateEditor(snapshot);
        var pendingAnalysis = new TaskCompletionSource<BikeEditorAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.Frame.ShockStroke = snapshot.ShockStroke + 1;
        analysis.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(pendingAnalysis.Task);

        var resetTask = editor.ResetCommand.ExecuteAsync(null);

        Assert.True(editor.Analysis.IsPlotBusy);

        pendingAnalysis.SetResult(new BikeEditorAnalysisResult.NotApplicable());
        await resetTask;

        Assert.False(editor.Analysis.IsPlotBusy);
    }

    [AvaloniaFact]
    public async Task Unloaded_DropsPendingAnalysisResult()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot, isNew: true);
        var pendingAnalysis = new TaskCompletionSource<BikeEditorAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        analysis.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(pendingAnalysis.Task);

        var loadedTask = editor.LoadedCommand.ExecuteAsync(null);
        editor.UnloadedCommand.Execute(null);

        pendingAnalysis.SetResult(new BikeEditorAnalysisResult.Computed(
            PresentationData(new CoordinateList([1, 2], [3, 4]))));
        await loadedTask;

        Assert.Null(editor.Analysis.LeverageRatioData);
    }
}
