using Sufni.App.Sessions.Models;
using System;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;

namespace Sufni.App.Sessions.Services.Preferences;

public interface ISessionPreferences
{
    Task<SessionPreferences> GetRecordedAsync(Guid sessionId);

    Task UpdateRecordedAsync(Guid sessionId, Func<SessionPreferences, SessionPreferences> update);
    Task RemoveRecordedAsync(Guid sessionId);

    IObservable<SessionPreferences> ObserveRecorded(Guid sessionId);
}
