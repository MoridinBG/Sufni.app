using System.Reactive.Linq;
using NSubstitute;
using Sufni.App.Models;
using Sufni.App.Services;

namespace Sufni.App.Tests.Infrastructure;

public static class SessionPreferencesTestExtensions
{
    // ObserveRecorded returns null on a bare substitute; SessionDetailViewModel
    // subscribes during Loaded and would NRE. Empty observable = no remote
    // sync notifications, which is the right default for tests that don't
    // exercise sync.
    public static ISessionPreferences WithDefaultObserveRecorded(this ISessionPreferences preferences)
    {
        preferences.ObserveRecorded(Arg.Any<Guid>()).Returns(Observable.Empty<SessionPreferences>());
        return preferences;
    }
}
