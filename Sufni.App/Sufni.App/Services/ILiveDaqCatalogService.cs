using System;
using System.Collections.Generic;

namespace Sufni.App.Services;

public sealed record LiveDaqCatalogEntry(
    string IdentityKey,
    string DisplayName,
    string? BoardId,
    string Host,
    int Port)
{
    public string Endpoint => $"{Host}:{Port}";
}

public interface ILiveDaqCatalogService
{
    IDisposable AcquireBrowse();

    IObservable<IReadOnlyList<LiveDaqCatalogEntry>> Observe();
}