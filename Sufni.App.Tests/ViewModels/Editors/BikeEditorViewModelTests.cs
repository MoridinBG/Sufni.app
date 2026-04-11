using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Headless.XUnit;
using Sufni.App.BikeEditing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.App.ViewModels.LinkageParts;
using Sufni.Kinematics;

namespace Sufni.App.Tests.ViewModels.Editors;

public class BikeEditorViewModelTests
{
    private readonly IBikeCoordinator bikeCoordinator = Substitute.For<IBikeCoordinator>();
    private readonly IBikeDependencyQuery dependencyQuery = Substitute.For<IBikeDependencyQuery>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();

    public BikeEditorViewModelTests()
    {
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(new BikeEditorAnalysisResult.Unavailable()));
        bikeCoordinator.LoadImageAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeImageLoadResult>(new BikeImageLoadResult.Canceled()));
        bikeCoordinator.ImportBikeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeImportResult>(new BikeImportResult.Canceled()));
        bikeCoordinator.ExportBikeAsync(Arg.Any<Bike>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeExportResult>(new BikeExportResult.Canceled()));
        dependencyQuery.Changes.Returns(Observable.Empty<Unit>());
    }

    private BikeEditorViewModel CreateEditor(BikeSnapshot snapshot, bool isNew = false) =>
        new(snapshot, isNew, bikeCoordinator, dependencyQuery, shell, dialogService);

    private static BikeAnalysisPresentationData PresentationData(CoordinateList leverageRatioData) =>
        new(leverageRatioData, new CoordinateList([], []));

    private static double WheelDiameter(EtrtoRimSize rimSize, double tireWidth) =>
        Math.Round(rimSize.CalculateTotalDiameterMm(tireWidth), 1);

    private static Linkage CreateFullSuspensionLinkage(bool includeHeadTubeJoints = false)
    {
        var mapping = new JointNameMapping();
        var bottomBracket = new Joint(mapping.BottomBracket, JointType.BottomBracket, 0, 0);
        var rearWheel = new Joint(mapping.RearWheel, JointType.RearWheel, 4, 0);
        var frontWheel = new Joint(mapping.FrontWheel, JointType.FrontWheel, 12, 1);
        var shockEye1 = new Joint(mapping.ShockEye1, JointType.Floating, 4, 3);
        var shockEye2 = new Joint(mapping.ShockEye2, JointType.Fixed, 0, 3);

        List<Joint> joints = [bottomBracket, rearWheel, frontWheel, shockEye1, shockEye2];
        if (includeHeadTubeJoints)
        {
            joints.Add(new Joint(mapping.HeadTube1, JointType.HeadTube, 10, 2));
            joints.Add(new Joint(mapping.HeadTube2, JointType.HeadTube, 9, 5));
        }

        var linkage = new Linkage
        {
            Joints = [.. joints],
            Links =
            [
                new Link(bottomBracket, rearWheel),
                new Link(rearWheel, shockEye1),
            ],
            Shock = new Link(shockEye1, shockEye2),
            ShockStroke = 0.5,
        };
        linkage.ResolveJoints();
        return linkage;
    }

    private static BikeSnapshot FullSuspensionSnapshot(
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
            Image = TestImages.SmallPng(),
            PixelsToMillimeters = 1,
            Linkage = CreateFullSuspensionLinkage(includeHeadTubeJoints),
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

    private static BikeSnapshot ForkOnlySnapshot(
        EtrtoRimSize? frontWheelRimSize = null,
        double? frontWheelTireWidth = null,
        double? frontWheelDiameter = null,
        EtrtoRimSize? rearWheelRimSize = null,
        double? rearWheelTireWidth = null,
        double? rearWheelDiameter = null,
        double imageRotationDegrees = 0,
        long updated = 1)
    {
        var bike = new Bike(Guid.NewGuid(), "fork only")
        {
            HeadAngle = 65,
            ForkStroke = 160,
            FrontWheelRimSize = frontWheelRimSize,
            FrontWheelTireWidth = frontWheelTireWidth,
            FrontWheelDiameterMm = frontWheelDiameter,
            RearWheelRimSize = rearWheelRimSize,
            RearWheelTireWidth = rearWheelTireWidth,
            RearWheelDiameterMm = rearWheelDiameter,
            Image = TestImages.SmallPng(),
            ImageRotationDegrees = imageRotationDegrees,
            Updated = updated,
        };

        return BikeSnapshot.From(bike);
    }

    private static IReadOnlyList<(string Name, JointType? Type, double X, double Y)> DescribeJoints(IEnumerable<Joint> joints) =>
        joints
            .OrderBy(joint => joint.Name)
            .Select(joint => (joint.Name ?? string.Empty, joint.Type, Math.Round(joint.X, 3), Math.Round(joint.Y, 3)))
            .ToList();

    private static IReadOnlyList<string> DescribeLinks(IEnumerable<Link> links) =>
        links
            .Select(DescribeLink)
            .OrderBy(link => link)
            .ToList();

    private static IReadOnlyList<(string Name, JointType? Type, double X, double Y)> DescribeEditorJoints(BikeEditorViewModel editor)
    {
        Assert.NotNull(editor.Image);
        Assert.True(editor.PixelsToMillimeters.HasValue);

        return editor.JointViewModels
            .Select(joint => joint.ToJoint(editor.Image!.Size.Height, editor.PixelsToMillimeters!.Value))
            .OrderBy(joint => joint.Name)
            .Select(joint => (joint.Name ?? string.Empty, joint.Type, Math.Round(joint.X, 3), Math.Round(joint.Y, 3)))
            .ToList();
    }

    private static IReadOnlyList<string> DescribeEditorLinks(BikeEditorViewModel editor)
    {
        Assert.NotNull(editor.Image);
        Assert.True(editor.PixelsToMillimeters.HasValue);

        return editor.LinkViewModels
            .Select(link => link.ToLink(editor.Image!.Size.Height, editor.PixelsToMillimeters!.Value))
            .Select(DescribeLink)
            .OrderBy(link => link)
            .ToList();
    }

    private static string DescribeLink(Link link)
    {
        var a = Assert.IsType<Joint>(link.A);
        var b = Assert.IsType<Joint>(link.B);
        return string.CompareOrdinal(a.Name, b.Name) <= 0
            ? $"{a.Name}->{b.Name}"
            : $"{b.Name}->{a.Name}";
    }

    private void AssertOpeningPreservesState(BikeSnapshot snapshot)
    {
        var editor = CreateEditor(snapshot);

        Assert.Equal(snapshot.FrontWheelRimSize, editor.FrontWheelRimSize);
        Assert.Equal(snapshot.FrontWheelTireWidth, editor.FrontWheelTireWidth);
        Assert.Equal(snapshot.FrontWheelDiameterMm, editor.FrontWheelDiameter);
        Assert.Equal(snapshot.RearWheelRimSize, editor.RearWheelRimSize);
        Assert.Equal(snapshot.RearWheelTireWidth, editor.RearWheelTireWidth);
        Assert.Equal(snapshot.RearWheelDiameterMm, editor.RearWheelDiameter);
        Assert.Equal(snapshot.ImageRotationDegrees, editor.ImageRotationDegrees);
        Assert.Equal(snapshot.Image is not null, editor.Image is not null);
        Assert.False(editor.IsDirty);
        Assert.False(editor.SaveCommand.CanExecute(null));

        if (snapshot.Linkage is null)
        {
            Assert.Empty(editor.JointViewModels);
            Assert.Empty(editor.LinkViewModels);
            return;
        }

        Assert.Equal(snapshot.Linkage.Joints.Count, editor.JointViewModels.Count);
        Assert.Equal(snapshot.Linkage.Links.Count + 1, editor.LinkViewModels.Count);
        Assert.Equal(DescribeJoints(snapshot.Linkage.Joints), DescribeEditorJoints(editor));
        Assert.Equal(DescribeLinks(snapshot.Linkage.Links.Append(snapshot.Linkage.Shock)), DescribeEditorLinks(editor));
    }

    // ----- Construction -----

    [AvaloniaFact]
    public void Construction_FromExistingForkOnlySnapshot_CopiesFields_AndIsNotDirty()
    {
        var snapshot = TestSnapshots.Bike(name: "trail bike", updated: 7);
        var editor = CreateEditor(snapshot);

        Assert.Equal(snapshot.Id, editor.Id);
        Assert.Equal("trail bike", editor.Name);
        Assert.Equal(snapshot.HeadAngle, editor.HeadAngle);
        Assert.Equal(snapshot.ForkStroke, editor.ForksStroke);
        Assert.Null(editor.ShockStroke);
        Assert.Null(editor.Image);
        Assert.True(editor.IsInDatabase);
        Assert.Equal(7, editor.BaselineUpdated);
        Assert.False(editor.IsDirty);
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.Empty(editor.JointViewModels);
        Assert.Empty(editor.LinkViewModels);
    }

    [AvaloniaFact]
    public void Construction_NewBike_AddsInitialJoints()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot, isNew: true);

        // AddInitialJoints contributes 7 joints (FrontWheel, BottomBracket,
        // RearWheel, two HeadTube points, two ShockEye floating points)
        // and the shock LinkViewModel.
        Assert.Equal(7, editor.JointViewModels.Count);
        Assert.Single(editor.LinkViewModels);
    }

    [AvaloniaFact]
    public void Construction_FromForkOnlySnapshotWithManualWheelState_PreservesOpeningInvariant()
    {
        var snapshot = ForkOnlySnapshot(
            frontWheelDiameter: 760,
            rearWheelDiameter: 750,
            imageRotationDegrees: 12.5,
            updated: 7);

        AssertOpeningPreservesState(snapshot);
    }

    [AvaloniaFact]
    public void Construction_FromFullSuspensionSnapshotWithHeadTubeJoints_PreservesOpeningInvariant()
    {
        var snapshot = FullSuspensionSnapshot(
            includeHeadTubeJoints: true,
            frontWheelRimSize: EtrtoRimSize.Inch29,
            frontWheelTireWidth: 2.4,
            frontWheelDiameter: WheelDiameter(EtrtoRimSize.Inch29, 2.4),
            rearWheelRimSize: EtrtoRimSize.Inch275,
            rearWheelTireWidth: 2.5,
            rearWheelDiameter: WheelDiameter(EtrtoRimSize.Inch275, 2.5),
            imageRotationDegrees: 12.5,
            updated: 7);

        AssertOpeningPreservesState(snapshot);
    }

    [AvaloniaFact]
    public void Construction_FromLegacyFullSuspensionSnapshotWithoutHeadTubeJoints_PreservesOpeningInvariant()
    {
        var snapshot = FullSuspensionSnapshot(
            includeHeadTubeJoints: false,
            frontWheelRimSize: EtrtoRimSize.Inch29,
            frontWheelTireWidth: 2.4,
            frontWheelDiameter: WheelDiameter(EtrtoRimSize.Inch29, 2.4),
            rearWheelRimSize: EtrtoRimSize.Inch275,
            rearWheelTireWidth: 2.5,
            rearWheelDiameter: WheelDiameter(EtrtoRimSize.Inch275, 2.5),
            imageRotationDegrees: 12.5,
            updated: 7);

        AssertOpeningPreservesState(snapshot);
    }

    [AvaloniaFact]
    public void Construction_PreservesPersistedHeadAngle_WhenItDiffersFromDerivedGeometry()
    {
        var snapshot = FullSuspensionSnapshot(
            includeHeadTubeJoints: true,
            frontWheelRimSize: EtrtoRimSize.Inch29,
            frontWheelTireWidth: 2.4,
            frontWheelDiameter: WheelDiameter(EtrtoRimSize.Inch29, 2.4),
            rearWheelRimSize: EtrtoRimSize.Inch275,
            rearWheelTireWidth: 2.5,
            rearWheelDiameter: WheelDiameter(EtrtoRimSize.Inch275, 2.5),
            imageRotationDegrees: 12.5,
            updated: 7) with
        {
            HeadAngle = 79.9,
        };

        var editor = CreateEditor(snapshot);

        Assert.Equal(79.9, editor.HeadAngle);
    }

    // ----- Dirtiness -----

    [AvaloniaFact]
    public void EditingName_FlipsIsDirtyTrue()
    {
        var snapshot = TestSnapshots.Bike(name: "before");
        var editor = CreateEditor(snapshot);
        Assert.False(editor.IsDirty);

        editor.Name = "after";

        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void EditingHeadAngle_FlipsIsDirtyTrue()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        Assert.False(editor.IsDirty);

        editor.HeadAngle = snapshot.HeadAngle + 1;

        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void SettingFrontWheelRimAndTire_ComputesDiameter_AndPreservesInputs()
    {
        var editor = CreateEditor(TestSnapshots.Bike(), isNew: true);

        editor.FrontWheelRimSize = EtrtoRimSize.Inch29;
        editor.FrontWheelTireWidth = 2.4;

        Assert.Equal(EtrtoRimSize.Inch29, editor.FrontWheelRimSize);
        Assert.Equal(2.4, editor.FrontWheelTireWidth);
        Assert.NotNull(editor.FrontWheelDiameter);
        Assert.Equal(WheelDiameter(EtrtoRimSize.Inch29, 2.4), editor.FrontWheelDiameter.Value, 3);
    }

    [AvaloniaFact]
    public void SettingFrontWheelDiameterManually_ClearsDerivedRimAndTireState()
    {
        var editor = CreateEditor(TestSnapshots.Bike(), isNew: true);
        editor.FrontWheelRimSize = EtrtoRimSize.Inch29;
        editor.FrontWheelTireWidth = 2.4;

        editor.FrontWheelDiameter = 700;

        Assert.Null(editor.FrontWheelRimSize);
        Assert.Null(editor.FrontWheelTireWidth);
        Assert.Equal(700, editor.FrontWheelDiameter);
    }

    [AvaloniaFact]
    public void SaveCommand_Disabled_WhenOnlyOneWheelIsConfigured()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";
        editor.FrontWheelDiameter = 760;

        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void RemovingJoint_DetachesPropertyHandler_FromRemovedInstance()
    {
        var editor = CreateEditor(TestSnapshots.Bike(), isNew: true);
        var removedJoint = new JointViewModel("Detached point", JointType.Floating, 10, 10);
        editor.JointViewModels.Add(removedJoint);
        editor.JointViewModels.Remove(removedJoint);
        var isDirtyBeforeMutation = editor.IsDirty;
        var canSaveBeforeMutation = editor.SaveCommand.CanExecute(null);

        removedJoint.Name = "Detached point renamed";

        Assert.Equal(isDirtyBeforeMutation, editor.IsDirty);
        Assert.Equal(canSaveBeforeMutation, editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void RemovingLink_DetachesPropertyHandler_FromRemovedInstance()
    {
        var editor = CreateEditor(TestSnapshots.Bike(), isNew: true);
        var removedLink = new LinkViewModel(editor.JointViewModels[0], editor.JointViewModels[1]);
        editor.LinkViewModels.Add(removedLink);
        editor.LinkViewModels.Remove(removedLink);
        var isDirtyBeforeMutation = editor.IsDirty;
        var canSaveBeforeMutation = editor.SaveCommand.CanExecute(null);

        removedLink.A = editor.JointViewModels[2];

        Assert.Equal(isDirtyBeforeMutation, editor.IsDirty);
        Assert.Equal(canSaveBeforeMutation, editor.SaveCommand.CanExecute(null));
    }

    // ----- CanSave -----

    [AvaloniaFact]
    public void SaveCommand_Disabled_WhenForkStrokeMissing()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";   // make dirty
        editor.ForksStroke = null; // invalidate

        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void SaveCommand_Enabled_WhenForkOnlyAndChanged()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        // CanSave allows fork-only bikes when ShockStroke is null.
        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    // ----- Save (fork-only path) -----

    [AvaloniaFact]
    public async Task Save_OnForkOnlyBike_HappyPath_RoutesThroughCoordinator()
    {
        var snapshot = TestSnapshots.Bike(updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";
        editor.FrontWheelDiameter = 760;
        editor.RearWheelDiameter = 750;
        editor.ImageRotationDegrees = 13.5;

        bikeCoordinator.SaveAsync(Arg.Any<Bike>(), 5)
            .Returns(new BikeSaveResult.Saved(11, new BikeEditorAnalysisResult.Unavailable()));
        TestApp.SetIsDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        await bikeCoordinator.Received(1).SaveAsync(
            Arg.Is<Bike>(b =>
                b.Id == snapshot.Id &&
                b.Name == "renamed" &&
                b.Linkage == null &&
                b.FrontWheelDiameterMm == 760 &&
                b.RearWheelDiameterMm == 750 &&
                b.ImageRotationDegrees == 13.5),
            5);
        Assert.Equal(11, editor.BaselineUpdated);
        Assert.Empty(editor.ErrorMessages);
        shell.DidNotReceive().GoBack();
    }

    [AvaloniaFact]
    public async Task Save_OnForkOnlyBike_OnMobile_NavigatesBack()
    {
        var snapshot = TestSnapshots.Bike(updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        bikeCoordinator.SaveAsync(Arg.Any<Bike>(), 5)
            .Returns(new BikeSaveResult.Saved(11, new BikeEditorAnalysisResult.Unavailable()));
        TestApp.SetIsDesktop(false);

        await editor.SaveCommand.ExecuteAsync(null);

        shell.Received(1).GoBack();
    }

    [AvaloniaFact]
    public async Task Save_OnForkOnlyBike_OnConflict_PromptsUser_AndReloadsWhenAccepted()
    {
        var snapshot = TestSnapshots.Bike(name: "old", updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        var fresh = TestSnapshots.Bike(id: snapshot.Id, name: "remote-updated", updated: 12);
        bikeCoordinator.SaveAsync(Arg.Any<Bike>(), 5)
            .Returns(new BikeSaveResult.Conflict(fresh));
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        TestApp.SetIsDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("remote-updated", editor.Name);
        Assert.Equal(12, editor.BaselineUpdated);
        Assert.False(editor.IsDirty);
        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Save_OnForkOnlyBike_OnConflict_Accepted_QueuesFreshAnalysisRefresh()
    {
        var snapshot = TestSnapshots.Bike(name: "old", updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        var fresh = TestSnapshots.Bike(id: snapshot.Id, name: "remote-updated", updated: 12);
        var pendingAnalysis = new TaskCompletionSource<BikeEditorAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        bikeCoordinator.SaveAsync(Arg.Any<Bike>(), 5)
            .Returns(new BikeSaveResult.Conflict(fresh));
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>())
            .Returns(pendingAnalysis.Task);
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        TestApp.SetIsDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        await bikeCoordinator.Received(1).LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>());
        Assert.True(editor.IsPlotBusy);

        pendingAnalysis.SetResult(new BikeEditorAnalysisResult.Unavailable());
        await Task.Yield();

        Assert.False(editor.IsPlotBusy);
    }

    [AvaloniaFact]
    public async Task Save_OnForkOnlyBike_OnConflict_DoesNothing_WhenUserDeclinesReload()
    {
        var snapshot = TestSnapshots.Bike(name: "old", updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        var fresh = TestSnapshots.Bike(id: snapshot.Id, name: "remote-updated", updated: 12);
        bikeCoordinator.SaveAsync(Arg.Any<Bike>(), 5)
            .Returns(new BikeSaveResult.Conflict(fresh));
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        TestApp.SetIsDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("renamed", editor.Name);
        Assert.Equal(5, editor.BaselineUpdated);
    }

    [AvaloniaFact]
    public async Task Save_OnForkOnlyBike_OnFailed_AppendsErrorMessage()
    {
        var snapshot = TestSnapshots.Bike(updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        bikeCoordinator.SaveAsync(Arg.Any<Bike>(), 5)
            .Returns(new BikeSaveResult.Failed("disk full"));
        TestApp.SetIsDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Single(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Save_OnInvalidLinkage_AppendsErrorMessage()
    {
        var snapshot = TestSnapshots.Bike(updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        bikeCoordinator.SaveAsync(Arg.Any<Bike>(), 5)
            .Returns(new BikeSaveResult.InvalidLinkage());
        TestApp.SetIsDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Single(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Save_OnDesktop_AppliesAnalysisFromSavedResult_WithoutReloadingAnalysis()
    {
        var snapshot = TestSnapshots.Bike(updated: 5);
        var editor = CreateEditor(snapshot);
        var data = new CoordinateList([1, 2], [3, 4]);
        editor.Name = "renamed";

        bikeCoordinator.SaveAsync(Arg.Any<Bike>(), 5)
            .Returns(new BikeSaveResult.Saved(
                11,
                new BikeEditorAnalysisResult.Computed(PresentationData(data))));
        TestApp.SetIsDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(data, editor.LeverageRatioData);
        await bikeCoordinator.DidNotReceive().LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>());
    }

    // ----- CanDelete -----

    [AvaloniaFact]
    public void DeleteCommand_Enabled_WhenNotInDatabase()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot, isNew: true);

        Assert.True(editor.DeleteCommand.CanExecute(true));
    }

    [AvaloniaFact]
    public void DeleteCommand_Disabled_WhenDependencyQuerySaysInUse()
    {
        var snapshot = TestSnapshots.Bike();
        dependencyQuery.IsBikeInUse(snapshot.Id).Returns(true);
        var editor = CreateEditor(snapshot);

        Assert.False(editor.DeleteCommand.CanExecute(true));
    }

    // ----- Delete -----

    [AvaloniaFact]
    public async Task Delete_OnNewUnsavedBike_NavigatesBack_WithoutCallingCoordinator()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot, isNew: true);

        await editor.DeleteCommand.ExecuteAsync(true);

        await bikeCoordinator.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
        shell.Received(1).GoBack();
    }

    [AvaloniaFact]
    public async Task Delete_HappyPath_RoutesThroughCoordinator_AndNavigatesBack()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        bikeCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new BikeDeleteResult(BikeDeleteOutcome.Deleted));

        await editor.DeleteCommand.ExecuteAsync(true);

        await bikeCoordinator.Received(1).DeleteAsync(snapshot.Id);
        shell.Received(1).GoBack();
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Delete_InUse_AppendsErrorMessage_AndDoesNotNavigateBack()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        bikeCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new BikeDeleteResult(BikeDeleteOutcome.InUse));

        await editor.DeleteCommand.ExecuteAsync(true);

        Assert.Single(editor.ErrorMessages);
        shell.DidNotReceive().GoBack();
    }

    [AvaloniaFact]
    public async Task Delete_Failed_AppendsErrorMessage_AndDoesNotNavigateBack()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        bikeCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new BikeDeleteResult(BikeDeleteOutcome.Failed, "locked"));

        await editor.DeleteCommand.ExecuteAsync(true);

        Assert.Single(editor.ErrorMessages);
        shell.DidNotReceive().GoBack();
    }

    [AvaloniaFact]
    public async Task Loaded_AppliesComputedAnalysisResult()
    {
        var snapshot = FullSuspensionSnapshot(
            frontWheelDiameter: 760,
            rearWheelDiameter: 750);
        var editor = CreateEditor(snapshot);
        var data = new CoordinateList([1, 2], [3, 4]);
        var rearAxlePathData = new CoordinateList([2, 4], [0.25, 0.75]);
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(
                new BikeEditorAnalysisResult.Computed(new BikeAnalysisPresentationData(data, rearAxlePathData))));

        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Equal(data, editor.LeverageRatioData);
        Assert.Equal(2, editor.RearAxlePath.Count);
        Assert.Equal(2, editor.RearAxlePath[0].X, 3);
        Assert.Equal(0.75, editor.RearAxlePath[0].Y, 3);
        Assert.Equal(4, editor.RearAxlePath[1].X, 3);
        Assert.Equal(0.25, editor.RearAxlePath[1].Y, 3);
    }

    [AvaloniaFact]
    public async Task Loaded_ClearsLeverageRatioData_WhenAnalysisUnavailable()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        editor.LeverageRatioData = new CoordinateList([1, 2], [3, 4]);
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(new BikeEditorAnalysisResult.Unavailable()));

        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Null(editor.LeverageRatioData);
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Loaded_ClearsLeverageRatioData_AndAppendsError_WhenAnalysisFails()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        editor.LeverageRatioData = new CoordinateList([1, 2], [3, 4]);
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(new BikeEditorAnalysisResult.Failed("boom")));

        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Null(editor.LeverageRatioData);
        Assert.Single(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Import_AppliesImportedBike_AndResetsBaselineToDraft()
    {
        var snapshot = TestSnapshots.Bike(updated: 5, name: "old bike");
        var editor = CreateEditor(snapshot);
        var importedBike = new Bike(Guid.NewGuid(), "imported bike")
        {
            HeadAngle = 63,
            ForkStroke = 170,
            FrontWheelRimSize = EtrtoRimSize.Inch29,
            FrontWheelTireWidth = 2.4,
            FrontWheelDiameterMm = WheelDiameter(EtrtoRimSize.Inch29, 2.4),
            RearWheelRimSize = EtrtoRimSize.Inch275,
            RearWheelTireWidth = 2.5,
            RearWheelDiameterMm = WheelDiameter(EtrtoRimSize.Inch275, 2.5),
            ImageRotationDegrees = 12.5,
        };
        bikeCoordinator.ImportBikeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeImportResult>(
                new BikeImportResult.Imported(new ImportedBikeEditorData(
                    importedBike,
                    new BikeEditorAnalysisResult.Unavailable()))));

        await editor.ImportCommand.ExecuteAsync(null);

        Assert.Equal("imported bike", editor.Name);
        Assert.Equal(63, editor.HeadAngle);
        Assert.Equal(170, editor.ForksStroke);
        Assert.Equal(EtrtoRimSize.Inch29, editor.FrontWheelRimSize);
        Assert.Equal(2.4, editor.FrontWheelTireWidth);
        Assert.Equal(WheelDiameter(EtrtoRimSize.Inch29, 2.4), editor.FrontWheelDiameter);
        Assert.Equal(EtrtoRimSize.Inch275, editor.RearWheelRimSize);
        Assert.Equal(2.5, editor.RearWheelTireWidth);
        Assert.Equal(WheelDiameter(EtrtoRimSize.Inch275, 2.5), editor.RearWheelDiameter);
        Assert.Equal(12.5, editor.ImageRotationDegrees);
        Assert.Equal(0, editor.BaselineUpdated);
        Assert.False(editor.IsInDatabase);
    }

    [AvaloniaFact]
    public async Task Reset_ShowsPlotBusyWhileAnalysisRefreshRuns()
    {
        var snapshot = TestSnapshots.Bike(name: "old bike");
        var editor = CreateEditor(snapshot);
        var pendingAnalysis = new TaskCompletionSource<BikeEditorAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.Name = "renamed";
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>())
            .Returns(pendingAnalysis.Task);

        var resetTask = editor.ResetCommand.ExecuteAsync(null);

        Assert.True(editor.IsPlotBusy);

        pendingAnalysis.SetResult(new BikeEditorAnalysisResult.Unavailable());
        await resetTask;

        Assert.False(editor.IsPlotBusy);
    }

    [AvaloniaFact]
    public async Task Reset_RestoresAcceptedSnapshot_AndClearsDirtyState()
    {
        var snapshot = FullSuspensionSnapshot(
            includeHeadTubeJoints: true,
            frontWheelRimSize: EtrtoRimSize.Inch29,
            frontWheelTireWidth: 2.4,
            frontWheelDiameter: WheelDiameter(EtrtoRimSize.Inch29, 2.4),
            rearWheelRimSize: EtrtoRimSize.Inch275,
            rearWheelTireWidth: 2.5,
            rearWheelDiameter: WheelDiameter(EtrtoRimSize.Inch275, 2.5),
            imageRotationDegrees: 12.5,
            updated: 7);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";
        editor.ImageRotationDegrees = 22.5;

        await editor.ResetCommand.ExecuteAsync(null);

        Assert.Equal(snapshot.Name, editor.Name);
        Assert.Equal(snapshot.ImageRotationDegrees, editor.ImageRotationDegrees);
        Assert.False(editor.IsDirty);
        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Unloaded_DropsPendingAnalysisResult()
    {
        var snapshot = TestSnapshots.Bike();
        var editor = CreateEditor(snapshot);
        var pendingAnalysis = new TaskCompletionSource<BikeEditorAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>())
            .Returns(pendingAnalysis.Task);

        var loadedTask = editor.LoadedCommand.ExecuteAsync(null);
        editor.UnloadedCommand.Execute(null);

        pendingAnalysis.SetResult(new BikeEditorAnalysisResult.Computed(
            PresentationData(new CoordinateList([1, 2], [3, 4]))));
        await loadedTask;

        Assert.Null(editor.LeverageRatioData);
    }

    [AvaloniaFact]
    public async Task Import_SupersedesEarlierPendingAnalysisResult()
    {
        var snapshot = TestSnapshots.Bike(updated: 5, name: "old bike");
        var editor = CreateEditor(snapshot);
        var pendingAnalysis = new TaskCompletionSource<BikeEditorAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingImport = new TaskCompletionSource<BikeImportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var importedData = new CoordinateList([5, 6], [7, 8]);

        bikeCoordinator.LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>())
            .Returns(pendingAnalysis.Task);
        bikeCoordinator.ImportBikeAsync(Arg.Any<CancellationToken>())
            .Returns(pendingImport.Task);

        var loadedTask = editor.LoadedCommand.ExecuteAsync(null);
        var importTask = editor.ImportCommand.ExecuteAsync(null);

        pendingAnalysis.SetResult(new BikeEditorAnalysisResult.Computed(
            PresentationData(new CoordinateList([1, 2], [3, 4]))));
        pendingImport.SetResult(new BikeImportResult.Imported(
            new ImportedBikeEditorData(
                new Bike(Guid.NewGuid(), "imported bike")
                {
                    HeadAngle = 63,
                    ForkStroke = 170,
                },
                new BikeEditorAnalysisResult.Computed(PresentationData(importedData)))));

        await loadedTask;
        await importTask;

        Assert.Equal("imported bike", editor.Name);
        Assert.Equal(importedData, editor.LeverageRatioData);
    }
}
