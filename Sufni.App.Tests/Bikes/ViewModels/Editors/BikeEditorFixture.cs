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

public abstract class BikeEditorFixture
{
    protected readonly IRearSuspensionAnalysisQuery analysis = TestCoordinatorSubstitutes.BikeAnalysis();
    protected readonly IBikeEditorService files = TestCoordinatorSubstitutes.BikeFiles();
    protected readonly IBikeDraftLoader draftLoader = TestCoordinatorSubstitutes.BikeDraftLoader();
    protected readonly IBikeCoordinator bikeCoordinator = TestCoordinatorSubstitutes.Bike();
    protected readonly IBikeDependencyQuery dependencyQuery = Substitute.For<IBikeDependencyQuery>();
    protected readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    protected readonly IDialogService dialogService = Substitute.For<IDialogService>();
    protected readonly IUiThreadDispatcher uiThreadDispatcher = new InlineUiThreadDispatcher();
    protected readonly FakeTimeProvider timeProvider = new();

    protected BikeEditorFixture()
    {
        dependencyQuery.Changes.Returns(Observable.Empty<Unit>());
    }

    protected BikeEditorViewModel CreateEditor(BikeSnapshot snapshot, bool isNew = false, bool hasPointer = true, IBackgroundTaskRunner? background = null, byte[]? storedImage = null)
    {
        draftLoader.LoadDraftAsync(snapshot.Id, Arg.Any<CancellationToken>()).Returns(new BikeDraftLoadResult.Loaded(storedImage is null ? DraftFrom(snapshot) : BikeDraft.From(snapshot, storedImage)));
        var editor = new BikeEditorViewModel(snapshot, isNew, bikeCoordinator, draftLoader, analysis, files, dependencyQuery, shell, dialogService, uiThreadDispatcher, new InputCapabilities(hasPointer, !hasPointer, hasPointer, !hasPointer, CommandModifier.Control), background ?? new InlineBackgroundTaskRunner(), timeProvider: timeProvider);
        return editor;
    }

    protected static BikeDraft DraftFrom(BikeSnapshot snapshot) =>
        BikeDraft.From(snapshot, snapshot.HasImage ? TestImages.SmallPngBytes() : []);

    protected static BikeAnalysisPresentationData PresentationData(CoordinateList leverageRatioData) =>
        new(leverageRatioData, new CoordinateList([], []));


    protected static BikeSnapshot FullSuspensionSnapshot(
        bool includeHeadTubeJoints = false,
        EtrtoRimSize? frontWheelRimSize = null,
        double? frontWheelTireWidth = null,
        double? frontWheelDiameter = null,
        EtrtoRimSize? rearWheelRimSize = null,
        double? rearWheelTireWidth = null,
        double? rearWheelDiameter = null,
        double imageRotationDegrees = 0,
        long updated = 1)
    {
        var bike = new Bike(Guid.NewGuid(), "full sus")
        {
            HeadAngle = 64,
            ForkStroke = 170,
            ImageBytes = TestImages.SmallPngBytes(),
            PixelsToMillimeters = 1,
            RearSuspension = new RearSuspensionSpec.Linkage(
                TestSnapshots.FullSuspensionLinkageSpec(includeHeadTubeJoints)),
            FrontWheelRimSize = frontWheelRimSize,
            FrontWheelTireWidth = frontWheelTireWidth,
            FrontWheelDiameterMm = frontWheelDiameter,
            RearWheelRimSize = rearWheelRimSize,
            RearWheelTireWidth = rearWheelTireWidth,
            RearWheelDiameterMm = rearWheelDiameter,
            ImageRotationDegrees = imageRotationDegrees,
            Updated = updated,
        };
        bike.ShockStroke = 0.5;
        return BikeSnapshot.From(bike);
    }
}
