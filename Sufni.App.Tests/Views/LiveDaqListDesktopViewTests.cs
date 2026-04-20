using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.DesktopViews.Controls;
using Sufni.App.DesktopViews.ItemLists;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.ItemLists;

namespace Sufni.App.Tests.Views;

public class LiveDaqListDesktopViewTests
{
    [AvaloniaFact]
    public async Task LiveDaqListDesktopView_RendersBoundLiveRows()
    {
        var coordinator = Substitute.For<ILiveDaqCoordinator>();
        var store = new LiveDaqStore();
        store.Upsert(new LiveDaqSnapshot(
            IdentityKey: "board-1",
            DisplayName: "Board 1",
            BoardId: "board-1",
            Host: "192.168.0.30",
            Port: 1557,
            IsOnline: true,
            SetupName: "Race",
            BikeName: "Demo"));

        var viewModel = new LiveDaqListViewModel(store, coordinator);
        await using var mounted = await MountAsync(viewModel);

        Assert.NotNull(mounted.View.FindControl<Control>("LiveDaqSearchBar"));

        var row = Assert.Single(mounted.View.FindAllVisual<LiveDaqListItemButton>());
        Assert.Equal("Board 1", row.FindControl<TextBlock>("DisplayNameTextBlock")!.Text);
        Assert.Equal("Race", row.FindControl<TextBlock>("SetupNameTextBlock")!.Text);
        Assert.Equal("Demo", row.FindControl<TextBlock>("BikeNameTextBlock")!.Text);
        Assert.Equal("192.168.0.30:1557", row.FindControl<TextBlock>("EndpointTextBlock")!.Text);
        Assert.True(row.FindControl<Border>("OnlineBadge")!.IsVisible);
        Assert.False(row.FindControl<Border>("OfflineBadge")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task LiveDaqListDesktopView_RowButtonCommand_SelectsIdentityKey()
    {
        var coordinator = Substitute.For<ILiveDaqCoordinator>();
        var store = new LiveDaqStore();
        store.Upsert(new LiveDaqSnapshot(
            IdentityKey: "board-1",
            DisplayName: "Board 1",
            BoardId: "board-1",
            Host: "192.168.0.30",
            Port: 1557,
            IsOnline: true,
            SetupName: "Race",
            BikeName: "Demo"));

        var viewModel = new LiveDaqListViewModel(store, coordinator);
        await using var mounted = await MountAsync(viewModel);

        var row = Assert.Single(mounted.View.FindAllVisual<LiveDaqListItemButton>());
        var openButton = row.FindControl<Button>("OpenButton");
        Assert.NotNull(openButton);
        openButton!.Command!.Execute(openButton.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        await coordinator.Received(1).SelectAsync("board-1");
    }

    private static async Task<MountedLiveDaqListDesktopView> MountAsync(LiveDaqListViewModel viewModel)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var view = new LiveDaqListDesktopView
        {
            DataContext = viewModel
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();
        return new MountedLiveDaqListDesktopView(host, view);
    }
}

internal sealed class MountedLiveDaqListDesktopView(Window host, LiveDaqListDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public LiveDaqListDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}