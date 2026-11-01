using System;
using System.Threading.Tasks;

using Sufni.App.Infrastructure.Theming;
namespace Sufni.App.Infrastructure;

public interface IAppPreferences
{
    IMapPreferences Map { get; }
    ISessionPreferences Session { get; }
    IThemePreferences Theme { get; }
    IUiPreferences Ui { get; }
    Task InitializeAsync();
}

public interface IUiPreferences
{
    Task SetLayoutProfileAsync(UiLayoutProfile? layoutProfile);
}

public interface IMapPreferences
{
    Task<Guid?> GetSelectedLayerIdAsync();
    Task SetSelectedLayerIdAsync(Guid selectedLayerId);
    IObservable<Guid?> ObserveChanges();
}

public interface IThemePreferences
{
    Task<SufniThemeMode> GetModeAsync();
    Task SetModeAsync(SufniThemeMode mode);
}

public interface ISessionPreferences
{
    Task<SessionPreferences> GetRecordedAsync(Guid sessionId);

    Task UpdateRecordedAsync(Guid sessionId, Func<SessionPreferences, SessionPreferences> update);
    Task RemoveRecordedAsync(Guid sessionId);

    IObservable<SessionPreferences> ObserveRecorded(Guid sessionId);
}
