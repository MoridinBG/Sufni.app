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
