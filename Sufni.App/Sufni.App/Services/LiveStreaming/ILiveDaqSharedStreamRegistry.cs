using System;
using Sufni.App.Stores;

namespace Sufni.App.Services.LiveStreaming;

public interface ILiveDaqSharedStreamReservation : IAsyncDisposable
{
    ILiveDaqSharedStream Stream { get; }
}

public interface ILiveDaqSharedStreamRegistry
{
    ILiveDaqSharedStream GetOrCreate(LiveDaqSnapshot snapshot);
    ILiveDaqSharedStreamReservation Reserve(LiveDaqSnapshot snapshot);
}