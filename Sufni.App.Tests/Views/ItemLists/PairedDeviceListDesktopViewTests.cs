using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.DesktopViews.Controls;
using Sufni.App.DesktopViews.ItemLists;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.ItemLists;

namespace Sufni.App.Tests.Views.ItemLists;

public class PairedDeviceListDesktopViewTests
{
    [AvaloniaFact]
    public async Task PairedDeviceListDesktopView_RendersBoundRows_AndStartsUndoWhenDeleteIsRequested()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var snapshot = new PairedDeviceSnapshot(
            DeviceId: "device-1",
            DisplayName: "Phone",
            Expires: new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        var store = new PairedDeviceStoreStub(snapshot);
        var coordinator = Substitute.For<IPairedDeviceCoordinator>();
        var viewModel = new PairedDeviceListViewModel(store, coordinator);
        var view = new PairedDeviceListDesktopView
        {
            DataContext = viewModel,
        };

        await using var mounted = await MountAsync(view);

        var row = Assert.Single(mounted.View.FindAllVisual<PairedDeviceListItemButton>());
        Assert.Contains(row.FindAllVisual<TextBlock>(), text => text.Text == "Phone");

        var deleteButton = row.FindControl<Button>("DeleteButton");
        Assert.NotNull(deleteButton);
        deleteButton!.Command!.Execute(deleteButton.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(viewModel.IsUndoVisible);
        Assert.Equal("Phone", viewModel.PendingName);
    }

    [AvaloniaFact]
    public async Task PairedDeviceListDesktopView_RendersNoRows_WhenStoreIsEmpty()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var view = new PairedDeviceListDesktopView
        {
            DataContext = new PairedDeviceListViewModel(new PairedDeviceStoreStub(), Substitute.For<IPairedDeviceCoordinator>()),
        };

        await using var mounted = await MountAsync(view);

        Assert.Empty(mounted.View.FindAllVisual<PairedDeviceListItemButton>());
    }

    private static async Task<MountedPairedDeviceListDesktopView> MountAsync(PairedDeviceListDesktopView view)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedPairedDeviceListDesktopView(host, view);
    }
}

internal sealed class MountedPairedDeviceListDesktopView(Window host, PairedDeviceListDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public PairedDeviceListDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}