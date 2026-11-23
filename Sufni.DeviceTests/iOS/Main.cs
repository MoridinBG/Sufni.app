using Microsoft.Testing.Extensions;
using Microsoft.Testing.Platform.Builder;

namespace Sufni.DeviceTests;

internal static class Program
{
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}

[Register("AppDelegate")]
internal sealed class AppDelegate : UIApplicationDelegate
{
    public override UISceneConfiguration GetConfiguration(UIApplication application,
        UISceneSession session, UISceneConnectionOptions options) =>
        new("Default Configuration", session.Role);
}

[Register("SceneDelegate")]
internal sealed class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions options)
    {
        Window = new UIWindow((UIWindowScene)scene) { RootViewController = new UIViewController() };
        Window.MakeKeyAndVisible();
        _ = Task.Run(RunTestsAsync);
    }

    private static async Task RunTestsAsync()
    {
        try
        {
            List<string> args = [.. Environment.GetCommandLineArgs().Skip(1)];
            if (!args.Contains("--results-directory"))
                args.AddRange(["--results-directory", Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TestResults")]);
            if (!args.Contains("--report-trx")) args.Add("--report-trx");
            var builder = await TestApplication.CreateBuilderAsync([.. args]);
            builder.AddMSTest(() => [typeof(RecordingTests).Assembly]);
            builder.AddTrxReportProvider();
            using var app = await builder.BuildAsync();
            Environment.Exit(await app.RunAsync());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Environment.Exit(1);
        }
    }
}
