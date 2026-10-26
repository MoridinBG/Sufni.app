using Sufni.App.ExtensionHost.Contracts.Services;

namespace Sufni.App.Tests.TestSupport.Doubles;

internal sealed class InMemorySecureStorage : ISecureStorage
{
    private readonly Dictionary<string, byte[]> byteValues = [];
    private readonly Dictionary<string, string> stringValues = [];

    public Task<byte[]?> GetAsync(string key) => Task.FromResult(byteValues.GetValueOrDefault(key));

    public Task<string?> GetStringAsync(string key) => Task.FromResult(stringValues.GetValueOrDefault(key));

    public Task SetAsync(string key, byte[]? value)
    {
        if (value is null)
        {
            byteValues.Remove(key);
        }
        else
        {
            byteValues[key] = value;
        }

        return Task.CompletedTask;
    }

    public Task SetStringAsync(string key, string? value)
    {
        if (value is null)
        {
            stringValues.Remove(key);
        }
        else
        {
            stringValues[key] = value;
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        byteValues.Remove(key);
        stringValues.Remove(key);
        return Task.CompletedTask;
    }

    public Task RemoveAllAsync()
    {
        byteValues.Clear();
        stringValues.Clear();
        return Task.CompletedTask;
    }
}
