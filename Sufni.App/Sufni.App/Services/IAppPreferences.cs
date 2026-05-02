using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sufni.App.Models;

namespace Sufni.App.Services;

public interface IAppPreferences
{
    IMapPreferences Map { get; }
    ISessionPreferences Session { get; }
}

public interface IMapPreferences
{
    Task<Guid?> GetSelectedLayerIdAsync();
    Task SetSelectedLayerIdAsync(Guid selectedLayerId);
    Task<IReadOnlyList<TileLayerConfig>> GetCustomLayersAsync();
    Task SetCustomLayersAsync(IReadOnlyList<TileLayerConfig> customLayers);
}

public interface ISessionPreferences
{
    Task<SessionPreferences> GetRecordedAsync(Guid sessionId);
    Task UpdateRecordedAsync(Guid sessionId, Func<SessionPreferences, SessionPreferences> update);
    Task RemoveRecordedAsync(Guid sessionId);
}
