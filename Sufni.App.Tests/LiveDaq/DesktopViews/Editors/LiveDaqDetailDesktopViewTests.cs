using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.LiveDaq.DesktopViews.Editors;
using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.LiveDaq.Views.Shared;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.LiveDaq;

namespace Sufni.App.Tests.LiveDaq.DesktopViews.Editors;

[Collection("Ui")]
public class LiveDaqDetailDesktopViewTests
{
    [AvaloniaFact]
    public async Task LiveDaqDetailDesktopView_RendersRequestedRatesAndManagementCard()
    {
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor();
        editor.RequestedTravelHz = 200;
        editor.RequestedImuHz = 100;
        editor.RequestedGpsFixHz = 5;

        await using var mounted = await MountAsync(editor);

        var requestedRates = mounted.View.FindControl<LiveDaqRequestedRatesGrid>("RequestedRatesGrid");
        var managementCard = mounted.View.FindControl<LiveDaqDeviceManagementCard>("DeviceManagementCard");

        Assert.NotNull(requestedRates);
        Assert.NotNull(managementCard);
        Assert.Equal(200, Convert.ToInt32(requestedRates!.FindControl<NumericUpDown>("RequestedTravelHzUpDown")!.Value));
        Assert.Equal(100, Convert.ToInt32(requestedRates.FindControl<NumericUpDown>("RequestedImuHzUpDown")!.Value));
        Assert.Equal(5, Convert.ToInt32(requestedRates.FindControl<NumericUpDown>("RequestedGpsFixHzUpDown")!.Value));
    }

    private static async Task<MountedLiveDaqDetailDesktopView> MountAsync(LiveDaqDetailViewModel editor)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var view = new LiveDaqDetailDesktopView
        {
            DataContext = editor
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();
        return new MountedLiveDaqDetailDesktopView(host, view);
    }
}

internal sealed class MountedLiveDaqDetailDesktopView(Window host, LiveDaqDetailDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public LiveDaqDetailDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
