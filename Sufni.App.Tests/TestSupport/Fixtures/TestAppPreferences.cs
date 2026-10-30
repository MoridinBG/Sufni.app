using Microsoft.Extensions.Time.Testing;
using Sufni.App.Infrastructure;

namespace Sufni.App.Tests.TestSupport.Fixtures;

internal static class TestAppPreferences
{
    public static AppPreferences Load(string path, TimeProvider? timeProvider = null) =>
        new(path, AppPreferences.ReadLaunchValues(path, UiLayoutProfile.Workspace), timeProvider ?? new FakeTimeProvider());
}
