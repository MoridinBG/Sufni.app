using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Services.Preferences;
using Sufni.App.Infrastructure;
using System;
using System.Threading.Tasks;

using Sufni.App.Infrastructure.Theming;
namespace Sufni.App.Shell.Services.Preferences;

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

