using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sufni.App.Desktop;
using Sufni.App.Infrastructure;
using Sufni.App.Video.Models;

namespace Sufni.App.Tests.Infrastructure;

public class AppCompositionTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void SharedGraph_ValidatesForDesktopAndroidAndIos(bool desktop, bool video)
    {
        var root = CreateRoot(desktop, video, video);
        var services = new ServiceCollection().AddSharedServices(root);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void SharedGraph_RejectsVideoCapabilityWithoutMatchingPlayer(bool video, bool registerPlayer)
    {
        var root = CreateRoot(false, video, registerPlayer);

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddSharedServices(root));
    }

    private static PlatformRoot CreateRoot(bool desktop, bool video, bool registerPlayer) =>
        new(
            new AppCapabilities(desktop, !desktop, desktop, true, video),
            new InputCapabilities(desktop, !desktop, desktop, !desktop),
            new LaunchPreferences(desktop ? UiLayoutProfile.Workspace : UiLayoutProfile.Compact),
            services =>
            {
                if (desktop)
                {
                    services.AddSingleton(Substitute.For<ISecureStorage>());
                    services.AddKeyedSingleton<IServiceDiscovery>("daq", Substitute.For<IServiceDiscovery>());
                    DesktopAppBootstrapper.RegisterDesktopSync(services);
                }
                else
                {
                    MobileAppBootstrapper.RegisterMobileSync(services,
                        () => Substitute.For<ISecureStorage>(),
                        () => Substitute.For<IFriendlyNameProvider>(),
                        _ => Substitute.For<IServiceDiscovery>(),
                        () => Substitute.For<IHapticFeedback>());
                }

                if (registerPlayer)
                {
                    services.AddSingleton(Substitute.For<IVideoPlayerFactory>());
                }
            },
            Substitute.For<IAppPaths>());
}
