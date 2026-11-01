using System.Threading.Tasks;

namespace Sufni.App.SyncAndPairing.Services;

public interface ISynchronizationTokenService
{
    Task<byte[]> GetSigningKeyAsync();
    Task<string> CreateAccessTokenAsync(string deviceId);
}
