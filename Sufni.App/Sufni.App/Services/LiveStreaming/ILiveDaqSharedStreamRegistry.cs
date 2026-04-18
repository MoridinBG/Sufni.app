using System;
using Sufni.App.Stores;

namespace Sufni.App.Services.LiveStreaming;

public interface ILiveDaqSharedStreamReservation : IAsyncDisposable
{
    ILiveDaqSharedStream Stream { get; }
}

public interface ILiveDaqSharedStreamRegistry
{
    ILiveDaqSharedStreamReservation Reserve(LiveDaqSnapshot snapshot);
}