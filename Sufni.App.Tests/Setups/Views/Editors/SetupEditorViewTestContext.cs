using Microsoft.Extensions.Time.Testing;
using System.Threading.Tasks;
using Avalonia.Controls;
using DynamicData;
using NSubstitute;

using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.DesktopViews.Editors;
using Sufni.App.Setups.Stores;
using Sufni.App.Setups.ViewModels.Editors;
using Sufni.App.Setups.Views.Editors;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Doubles;
namespace Sufni.App.Tests.Setups.Views.Editors;

internal sealed class SetupEditorViewTestContext : IDisposable
{
    private readonly BikeStoreStub bikeStore = new();
    private readonly ISetupCoordinator setupCoordinator = TestCoordinatorSubstitutes.Setup();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    public IShellCoordinator Shell => shell;

    public BikeSnapshot AddBike(string name = "test bike")
    {
        var bike = TestSnapshots.Bike(name: name) with
        {
            ShockStroke = 0.5,
            RearSuspension = new RearSuspensionSpec.Linkage(
                TestSnapshots.FullSuspensionLinkageSpec(includeHeadTubeJoints: true)),
        };
        bikeStore.Add(bike);
        return bike;
    }

    public void UpdateBike(BikeSnapshot bike) => bikeStore.Add(bike);

    public SetupEditorViewModel CreateEditor(SetupSnapshot snapshot, bool isNew = false)
    {
        return new SetupEditorViewModel(snapshot, isNew, bikeStore, setupCoordinator, shell, dialogService, new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider(), boardId: Guid.NewGuid());
    }

    public async Task<MountedSetupEditorView<SetupEditorView>> MountMobileAsync(SetupSnapshot snapshot, bool isNew = false)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Compact);

        var editor = CreateEditor(snapshot, isNew);
        var view = new SetupEditorView
        {
            DataContext = editor
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();
        return new MountedSetupEditorView<SetupEditorView>(host, view, editor);
    }

    public async Task<MountedSetupEditorView<SetupEditorDesktopView>> MountDesktopAsync(SetupSnapshot snapshot, bool isNew = false)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);

        var editor = CreateEditor(snapshot, isNew);
        var view = new SetupEditorDesktopView
        {
            DataContext = editor
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();
        return new MountedSetupEditorView<SetupEditorDesktopView>(host, view, editor);
    }

    public static SetupSnapshot CreateSetupSnapshot(BikeSnapshot bike)
    {
        return TestSnapshots.Setup(bikeId: bike.Id) with
        {
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
            {
                Length = 100,
                Resolution = 12
            }),
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new LinearShockSensorConfiguration
            {
                Length = 75,
                Resolution = 14
            })
        };
    }

    public void Dispose()
    {
        bikeStore.Dispose();
    }
}

internal sealed class MountedSetupEditorView<TView>(Window host, TView view, SetupEditorViewModel editor) : IAsyncDisposable
    where TView : Control
{
    public Window Host { get; } = host;
    public TView View { get; } = view;
    public SetupEditorViewModel Editor { get; } = editor;

    public async ValueTask DisposeAsync()
    {
        await Editor.PrepareCloseAsync();
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
