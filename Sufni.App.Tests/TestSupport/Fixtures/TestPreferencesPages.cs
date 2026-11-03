using Sufni.App.Sessions.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;

namespace Sufni.App.Tests.TestSupport.Fixtures;

internal static class TestPreferencesPages
{
    public static PreferencesPageViewModel Create(SignalDisplayPreferences? initial = null)
    {
        var current = initial ?? SessionPreferences.Default.SignalDisplay;
        PreferencesPageViewModel page = null!;
        page = new PreferencesPageViewModel(() => current, next =>
        {
            var previous = current;
            current = next;
            page.RefreshSignalDisplayPreferences(previous, next);
        });
        return page;
    }
}
