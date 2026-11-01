using Sufni.App.Infrastructure;
using Sufni.App.Tests.TestSupport.Async;

namespace Sufni.App.Tests.TestSupport.Fixtures;

internal static class TestAppPreferences
{
    public static AppPreferences Load(string path) =>
        new(path, AppPreferences.ReadLaunchValues(path, UiLayoutProfile.Workspace), new InlineBackgroundTaskRunner(), new InlineUiThreadDispatcher());
}
