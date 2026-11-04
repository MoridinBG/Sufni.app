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

    /// <summary>Replays committed values on the UI thread and publishes changes before the write completes.</summary>
    IObservable<SessionPreferences> ObserveRecorded(Guid sessionId);
}
