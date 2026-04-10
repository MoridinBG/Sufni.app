using System.Threading.Tasks;

namespace Sufni.App.Services;

public interface ISecureStorage
{
    Task<byte[]?> GetAsync(string key);

    Task<string?> GetStringAsync(string key);

    Task SetAsync(string key, byte[]? value);

    Task SetStringAsync(string key, string? value);

    Task RemoveAsync(string key);

    Task RemoveAllAsync();
}