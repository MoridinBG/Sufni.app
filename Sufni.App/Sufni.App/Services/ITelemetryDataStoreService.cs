using Sufni.App.Models;
using System.Collections.ObjectModel;

namespace Sufni.App.Services;

public interface ITelemetryDataStoreService
{
    public ObservableCollection<ITelemetryDataStore> DataStores { get; }
}
