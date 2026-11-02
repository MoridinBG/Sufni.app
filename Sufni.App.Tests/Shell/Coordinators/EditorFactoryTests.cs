using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Infrastructure;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Acquisition;
using NSubstitute;
using Sufni.App.Acquisition.ViewModels;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Setups.ViewModels.Editors;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.TestSupport.Doubles;

namespace Sufni.App.Tests.Shell.Coordinators;

public class EditorFactoryTests
{
    [Theory]
    [InlineData(UiLayoutProfile.Compact, true)]
    [InlineData(UiLayoutProfile.Compact, false)]
    [InlineData(UiLayoutProfile.Workspace, true)]
    [InlineData(UiLayoutProfile.Workspace, false)]
    public void CreateEditor_ModeEditingUsesInputCapabilityIndependentlyOfLayout(UiLayoutProfile layout, bool hasPointer)
    {
        var input = new InputCapabilities(hasPointer, !hasPointer, hasPointer, !hasPointer, CommandModifier.Control);
        var factory = TestEditorFactories.Create(appEnvironment: TestAppEnvironment.Create(layout, input: input));

        var editor = Assert.IsType<BikeEditorViewModel>(factory.CreateEditor(new EditorKey.NewBike(), Substitute.For<IShellCoordinator>()));

        Assert.Equal(hasPointer, editor.SetRearSuspensionModeCommand.CanExecute(Sufni.App.Bikes.Models.RearSuspensionKind.Linkage));
    }

    [Fact]
    public void CreateEditor_ImportedBikeIsAnIndependentUnsavedDraft()
    {
        var factory = TestEditorFactories.Create();
        var shell = Substitute.For<IShellCoordinator>();
        var draft = BikeDraft.From(TestSnapshots.Bike(name: "Imported bike"));
        var original = Assert.IsType<BikeEditorViewModel>(factory.CreateEditor(new EditorKey.NewBike(), shell));
        original.Name = "Unsaved original";

        var imported = Assert.IsType<BikeEditorViewModel>(factory.CreateEditor(new EditorKey.NewBike(draft), shell));

        Assert.Equal(draft.Id, imported.Id);
        Assert.Equal(draft.Name, imported.Name);
        Assert.True(imported.IsDirty);
        Assert.False(imported.IsInDatabase);
        Assert.Equal("Unsaved original", original.Name);
    }

    [Fact]
    public void CreateEditor_NewDraftsAreDirty()
    {
        var factory = TestEditorFactories.Create();
        var shell = Substitute.For<IShellCoordinator>();

        var bike = Assert.IsType<BikeEditorViewModel>(factory.CreateEditor(new EditorKey.NewBike(), shell));
        var setup = Assert.IsType<SetupEditorViewModel>(factory.CreateEditor(new EditorKey.NewSetup(), shell));

        Assert.True(bike.IsDirty);
        Assert.True(setup.IsDirty);
    }

    [Fact]
    public async Task CreateEditor_ImportTabsHaveIndependentLifetimes()
    {
        var harness = new ImportWorkflowHarness();
        var factory = TestEditorFactories.Create(importCoordinator: harness.ImportSessionsCoordinatorSubstitute);
        var shell = Substitute.For<IShellCoordinator>();
        harness.ImportState.OnNext(new SessionImportState.Running(1, 3, []));
        var first = Assert.IsType<ImportSessionsViewModel>(factory.CreateEditor(new EditorKey.ImportSessions(), shell));
        await first.PrepareCloseAsync();
        harness.ImportState.OnNext(new SessionImportState.Running(2, 3, []));

        var second = Assert.IsType<ImportSessionsViewModel>(factory.CreateEditor(new EditorKey.ImportSessions(), shell));

        Assert.NotSame(first, second);
        Assert.Equal(1, first.CurrentFileIndex);
        Assert.Equal(2, second.CurrentFileIndex);
        Assert.True(second.IsImporting);
        await second.PrepareCloseAsync();
    }
}
