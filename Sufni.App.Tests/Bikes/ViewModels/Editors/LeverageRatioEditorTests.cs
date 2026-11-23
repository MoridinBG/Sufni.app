using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Bikes.Services;
using Sufni.App.Shared.Models;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.Bikes.ViewModels.Editors;

[Collection("Ui")]
public class LeverageRatioEditorTests : BikeEditorFixture
{
    [AvaloniaFact]
    public async Task ImportLeverageRatio_FailurePreservesCurveAndSeparatesDiagnostics()
    {
        var curve = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25));
        var editor = CreateEditor(TestSnapshots.LeverageRatioBike(curve));
        files.ImportLeverageRatioAsync(Arg.Any<CancellationToken>())
            .Returns(new LeverageRatioImportResult.Failed(LeverageRatioImportFailureReason.Import, "disk full"));

        await editor.LeverageRatioEditor.ImportLeverageRatioCommand.ExecuteAsync(null);

        Assert.Equal(curve, editor.LeverageRatioEditor.Value);
        var failure = Assert.Single(editor.ErrorMessages);
        Assert.False(string.IsNullOrWhiteSpace(failure.Details));
        Assert.DoesNotContain("disk full", failure.Summary);
    }
}
