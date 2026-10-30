namespace Sufni.App.Tests.TestSupport.Harness;

/// <summary>Headless application without native services, persistence or XAML startup.</summary>
public sealed class TestApp : App
{
    public TestApp() : base(TestAppEnvironment.CreateRoot())
    {
    }

    public override void Initialize() { }
    public override void OnFrameworkInitializationCompleted() { }

}
