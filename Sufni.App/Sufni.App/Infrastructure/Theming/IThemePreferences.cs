using System;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;

namespace Sufni.App.Infrastructure.Theming;

public interface IThemePreferences
{
    Task<SufniThemeMode> GetModeAsync();
    Task SetModeAsync(SufniThemeMode mode);
}

