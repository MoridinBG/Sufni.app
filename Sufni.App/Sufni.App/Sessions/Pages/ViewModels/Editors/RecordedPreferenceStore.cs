using System;
using System.Threading.Tasks;

using Sufni.App.Infrastructure;
namespace Sufni.App.Sessions.Pages.ViewModels.Editors;

internal sealed class RecordedPreferenceStore
{
    private readonly ISessionPreferences sessionPreferences;
    private readonly Func<Guid> sessionId;
    private readonly Action<string> addError;
    private bool persistenceEnabled;

    public RecordedPreferenceStore(
        ISessionPreferences sessionPreferences,
        Func<Guid> sessionId,
        Action<string> addError)
    {
        this.sessionPreferences = sessionPreferences;
        this.sessionId = sessionId;
        this.addError = addError;
    }

    public SessionPreferences Current { get; private set; } = SessionPreferences.Default;

    public bool PersistenceEnabled => persistenceEnabled;

    public IObservable<SessionPreferences> Observe()
    {
        return sessionPreferences.ObserveRecorded(sessionId());
    }

    public async Task RestoreAsync(Action<SessionPreferences> apply)
    {
        persistenceEnabled = false;
        try
        {
            Apply(await sessionPreferences.GetRecordedAsync(sessionId()), apply);
        }
        catch (Exception e)
        {
            addError($"Session preferences could not be loaded: {e.Message}");
            Apply(SessionPreferences.Default, apply);
        }
        finally
        {
            persistenceEnabled = true;
        }
    }

    public void ApplyWithoutPersisting(SessionPreferences preferences, Action<SessionPreferences> apply)
    {
        persistenceEnabled = false;
        try
        {
            Apply(preferences, apply);
        }
        finally
        {
            persistenceEnabled = true;
        }
    }

    public void UpdateCurrent(Func<SessionPreferences, SessionPreferences> update)
    {
        Current = update(Current);
    }

    public void PersistChangeIfEnabled(Func<SessionPreferences, SessionPreferences> update)
    {
        if (!persistenceEnabled)
        {
            return;
        }

        _ = PersistChangeAsync(update);
    }

    public async Task<bool> PersistChangeAsync(Func<SessionPreferences, SessionPreferences> update)
    {
        try
        {
            await sessionPreferences.UpdateRecordedAsync(sessionId(), update);
            return true;
        }
        catch (Exception e)
        {
            addError($"Session preferences could not be saved: {e.Message}");
            return false;
        }
    }

    private void Apply(SessionPreferences preferences, Action<SessionPreferences> apply)
    {
        Current = preferences;
        apply(preferences);
    }
}
