using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Segments.DesktopViews;
using Sufni.App.Segments.Views;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Segments.DesktopViews;

[Collection("Ui")]
public class SegmentPickerFlyoutButtonViewTests
{
    [AvaloniaFact]
    public async Task Panel_KeepsThePicker_WhileTheFlyoutIsClosed()
    {
        using var picker = TestSegmentPickers.Create();
        var view = new SegmentPickerFlyoutButtonView { DataContext = picker };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        try
        {
            var flyout = Assert.IsType<Flyout>(view.FindControl<Button>("SegmentsButton")!.Flyout);
            var panel = Assert.IsType<SegmentPickerPanelView>(flyout.Content);
            Assert.Same(picker, panel.DataContext);
        }
        finally
        {
            host.Close();
        }
    }
}
