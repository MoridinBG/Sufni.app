using Avalonia;
using Avalonia.Headless;
using Sufni.App.Tests.Infrastructure;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Sufni.App.Tests.Infrastructure;

/// <summary>
/// Referenced via the assembly-level
/// <see cref="AvaloniaTestApplicationAttribute"/>. The
/// <c>Avalonia.Headless.XUnit</c> runner picks this up to spin up a
/// real-but-headless <see cref="Application"/> instance for tests
/// decorated with <c>[AvaloniaFact]</c> / <c>[AvaloniaTheory]</c>.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<TestApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions
        {
            UseHeadlessDrawing = true,
        });
}
