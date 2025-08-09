using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform;
using Sufni.App.Models;

namespace Sufni.App.Services;

public class HttpApiServiceStub : IHttpApiService
{
    private static readonly Guid SetupClashId = Guid.Parse("6afdbe77bff1429da05c3442d59c4461");
    private static readonly Guid SetupClashNewId = Guid.Parse("8b5b3f2ed8404fb6ace97e155d40e6e8");
    private static readonly Guid SetupOneSuspensionId = Guid.Parse("484f618cd3bd48828e991d55d54003f5");

    private static readonly byte[] SessionData;

    static HttpApiServiceStub()
    {
        using var assetLoader = AssetLoader.Open(new Uri("avares://Sufni.App/Assets/sample.psst"));
        using var binaryReader = new BinaryReader(assetLoader);
        SessionData = binaryReader.ReadBytes((int)assetLoader.Length);

        Sessions =
        [
            new Session(id: new Guid("396d9b75-8adb-44b9-b265-bcabeb031b26"), name: "session 1", description: "Test session #1", setup: SetupClashNewId, timestamp: 1686998748)
            {
                ProcessedData = SessionData,
            },
            new Session(id: new Guid("f2e2300f-ff25-47f1-85e8-836704a84984"), name: "session 2", description: "Test session #2", setup: SetupClashNewId, timestamp: 1682943649)
            {
                ProcessedData = SessionData,
            },
            new Session(id: new Guid("eea9800b-89b0-4369-a017-7788ec507f98"), name: "session 3", description: "Test session #3", setup: SetupClashNewId, timestamp: 1682760595)
        ];
    }

    private static readonly List<Setup> Setups = new()
    {
        new(SetupClashId, "Clash (Manitou,sensitive}"),
        new(SetupClashNewId, "Clash (new)"),
        new(SetupOneSuspensionId, "Setup with one Calibration"),
    };

    private static readonly List<Board> Boards = new()
    {
        new(Guid.Empty, SetupClashId),
        new(new Guid("00000000-0000-0080-8000-000000000001"), SetupClashNewId),
        new(new Guid("00000000-0000-0080-8011-223344556677"), SetupOneSuspensionId),
        new(new Guid("00000000-0000-0080-8000-000000000003"), null),
    };

    private static readonly List<Session> Sessions;

    public Task<string> RefreshTokensAsync(string url, string refreshToken)
    {
        return Task.FromResult(refreshToken);
    }

    public Task<string> RegisterAsync(string url, string username, string password)
    {
        return Task.FromResult("MOCK_TOKEN");
    }

    public Task UnregisterAsync(string refreshToken)
    {
        return Task.CompletedTask;
    }

    public Task<SynchronizationData> PullSyncAsync(int since = 0)
    {
        return Task.FromResult(new SynchronizationData
        {
            Boards = Boards,
            Setups = Setups,
            Sessions = Sessions
        });
    }

    public Task PushSyncAsync(SynchronizationData syncData)
    {
        return Task.CompletedTask;
    }

    public Task<List<Guid>> GetIncompleteSessionIdsAsync()
    {
        return Task.FromResult(Sessions.Where(s => s.ProcessedData is null).Select(s => s.Id).ToList());
    }

    public Task<byte[]?> GetSessionPsstAsync(Guid id)
    {
        return Task.FromResult(Sessions.First(s => s.Id == id).ProcessedData ?? null);
    }

    public Task PatchSessionPsstAsync(Guid id, byte[] data)
    {
        var s = Sessions.First(s => s.Id == id);
        s.ProcessedData = data;

        return Task.CompletedTask;
    }
}