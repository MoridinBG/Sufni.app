using System;

namespace Sufni.App.Services;

public interface ILiveDaqBrowseOwner
{
    IDisposable AcquireBrowse();
}