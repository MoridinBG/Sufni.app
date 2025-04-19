using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Microsoft.Extensions.DependencyInjection;
using SecureStorage;
using Sufni.App.Services;

[assembly: SupportedOSPlatform("browser")]

namespace Sufni.App.Browser;

internal partial class Program
{
    private static async Task Main(string[] _) => await BuildAvaloniaApp()
        .WithInterFont()
        .StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp()
    {
        RegisteredServices.Collection.AddSingleton<ISecureStorage, SecureStorage.SecureStorage>();
        return AppBuilder.Configure<App>();
    }
}