using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Tests.TestSupport.Acquisition;
using NSubstitute;
using Sufni.App.Acquisition.ViewModels;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.App.Setups.ViewModels.Editors;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.TestSupport.Doubles;

namespace Sufni.App.Tests.Shell.Coordinators;

public class EditorFactoryTests
{
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
