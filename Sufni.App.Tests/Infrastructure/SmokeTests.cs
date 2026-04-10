using Avalonia.Headless.XUnit;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Infrastructure;

public class SmokeTests
{
    [AvaloniaFact]
    public void HeadlessApp_Initializes_AndAppCurrent_IsTestApp()
    {
        Assert.NotNull(Sufni.App.App.Current);
        Assert.IsType<TestApp>(Sufni.App.App.Current);
    }

    [AvaloniaFact]
    public void TestImage_LoadsBitmap_WithKnownSize()
    {
        var bitmap = TestImages.SmallPng();
        Assert.Equal(1, bitmap.Size.Width);
        Assert.Equal(1, bitmap.Size.Height);
    }

    [AvaloniaFact]
    public void SetIsDesktop_FlipsAppFlag()
    {
        TestApp.SetIsDesktop(true);
        Assert.True(Sufni.App.App.Current!.IsDesktop);

        TestApp.SetIsDesktop(false);
        Assert.False(Sufni.App.App.Current!.IsDesktop);
    }
}
