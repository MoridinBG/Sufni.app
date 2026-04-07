using System;
using Sufni.App.Models;
using System.Collections.ObjectModel;

namespace Sufni.App.Services;

public interface ITelemetryDataStoreService
{
    public ObservableCollection<ITelemetryDataStore> DataStores { get; }
    public void StartBrowse();
    public void StopBrowse();

    /// <summary>
    /// One-shot probe for a mass-storage DAQ at this instant. Iterates
    /// the OS drives, finds the first one with a <c>BOARDID</c> file at
    /// the root, and returns its derived board id. Returns <c>null</c>
    /// if no DAQ drive is currently mounted. Pure read — does not touch
    /// the <see cref="DataStores"/> collection or the browse state, and
    /// does not create any side-effect directories on the drive.
    /// </summary>
    public Guid? DetectConnectedBoardId();
}
