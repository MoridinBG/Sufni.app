using System;

namespace Sufni.App.Services;

// Reference-counted owner for the shared `_gosst._tcp` DAQ browse session.
public interface IDaqBrowseOwner
{
    // Returns a lease for one caller. Dispose it when that caller no longer needs
    // discovery to stay active.
    IDisposable AcquireBrowse();
}
