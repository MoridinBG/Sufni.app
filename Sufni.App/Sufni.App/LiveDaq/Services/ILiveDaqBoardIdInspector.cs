using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.LiveDaq.Services;

/// <summary>Resolves a legacy v2 endpoint's board identity through IDENTIFY.</summary>
public interface ILiveDaqBoardIdInspector
{
    Task<BoardIdInspection> InspectAsync(IPAddress address, int port, CancellationToken cancellationToken = default);
}

public abstract record BoardIdInspection
{
    private BoardIdInspection() { }
    public sealed record Found(Guid BoardId) : BoardIdInspection;
    public sealed record TimedOut : BoardIdInspection;
    public sealed record Unreachable : BoardIdInspection;
}
