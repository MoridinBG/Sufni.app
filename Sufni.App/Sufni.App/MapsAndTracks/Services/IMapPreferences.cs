using System;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;

namespace Sufni.App.MapsAndTracks.Services;

public interface IMapPreferences
{
    Task<Guid?> GetSelectedLayerIdAsync();
    Task SetSelectedLayerIdAsync(Guid selectedLayerId);
    IObservable<Guid?> ObserveChanges();
}

