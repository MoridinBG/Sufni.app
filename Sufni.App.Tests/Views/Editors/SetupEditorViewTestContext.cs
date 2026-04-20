using System.Threading.Tasks;
using Avalonia.Controls;
using DynamicData;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.DesktopViews.Editors;
using Sufni.App.Models;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views.Editors;

namespace Sufni.App.Tests.Views.Editors;

internal sealed class SetupEditorViewTestContext : IDisposable
{
    private readonly IBikeStore bikeStore = Substitute.For<IBikeStore>();
    private readonly IBikeCoordinator bikeCoordinator = Substitute.For<IBikeCoordinator>();
    private readonly ISetupCoordinator setupCoordinator = Substitute.For<ISetupCoordinator>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly SourceCache<BikeSnapshot, Guid> bikesCache = new(snapshot => snapshot.Id);

    public SetupEditorViewTestContext()
    {
        bikeStore.Connect().Returns(bikesCache.Connect());
    }

    public IBikeCoordinator BikeCoordinator => bikeCoordinator;

    public BikeSnapshot AddBike(string name = "test bike")
    {
        var bike = TestSnapshots.Bike(name: name) with
        {
            RearSuspensionKind = RearSuspensionKind.Linkage,
            ShockStroke = 0.5,
            Linkage = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true),
        };
        bikesCache.AddOrUpdate(bike);
        return bike;
    }

    public SetupEditorViewModel CreateEditor(SetupSnapshot snapshot, bool isNew = false)
    {
        return new SetupEditorViewModel(snapshot, isNew, bikeStore, bikeCoordinator, setupCoordinator, shell, dialogService);
    }

    public async Task<MountedSetupEditorView<SetupEditorView>> MountMobileAsync(SetupSnapshot snapshot, bool isNew = false)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

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
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var editor = CreateEditor(snapshot, isNew);
        var view = new SetupEditorDesktopView
        {
            DataContext = editor
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();
        return new MountedSetupEditorView<SetupEditorDesktopView>(host, view, editor);
    }

    public static SetupSnapshot CreateSetupSnapshot(BikeSnapshot bike, Guid? boardId = null)
    {
        return TestSnapshots.Setup(bikeId: bike.Id, boardId: boardId ?? Guid.NewGuid()) with
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
        bikesCache.Dispose();
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
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}