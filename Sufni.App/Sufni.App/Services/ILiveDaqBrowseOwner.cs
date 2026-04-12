using System;

namespace Sufni.App.Services;

// Reference-counted owner for the shared `_gosst._tcp` browse session.
public interface ILiveDaqBrowseOwner
{
    // Returns a lease for one caller. Dispose it when that caller no longer needs
    // discovery to stay active.
    IDisposable AcquireBrowse();
}