using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Shared.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Bikes.ViewModels.Editors;

[Collection("Ui")]
public class BikeEditorAnalysisTests : BikeEditorFixture
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
        editor.LoadedCommand.Execute(null);
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

        editor.LoadedCommand.Execute(null);

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
        editor.LoadedCommand.Execute(null);
        Assert.NotNull(editor.Analysis.LeverageRatioData);
        analysis.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(new BikeEditorAnalysisResult.NotApplicable()));

        editor.LoadedCommand.Execute(null);

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
        editor.LoadedCommand.Execute(null);
        Assert.NotNull(editor.Analysis.LeverageRatioData);
        analysis.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(new BikeEditorAnalysisResult.Failed(BikeAnalysisFailureReason.Analyze, "boom")));

        editor.LoadedCommand.Execute(null);

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

        await resetTask;
        pendingAnalysis.SetResult(new BikeEditorAnalysisResult.NotApplicable());
        await ViewTestHelpers.FlushDispatcherAsync();

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

        editor.LoadedCommand.Execute(null);
        editor.UnloadedCommand.Execute(null);

        pendingAnalysis.SetResult(new BikeEditorAnalysisResult.Computed(
            PresentationData(new CoordinateList([1, 2], [3, 4]))));

        Assert.Null(editor.Analysis.LeverageRatioData);
    }
}
