using System;

namespace Sufni.App.Services;

public sealed class SessionDataArrivedEventArgs(Guid sessionId) : EventArgs
{
    public Guid SessionId { get; } = sessionId;
}
