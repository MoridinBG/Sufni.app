using System;
using System.Text;
using System.Threading.Tasks;
using Foundation;
using Security;
using Serilog;
using Sufni.App.Services;

namespace Sufni.App.macOS;

public class MacOsSecureStorage : ISecureStorage
{
    private static readonly ILogger logger = Log.ForContext<MacOsSecureStorage>();

    private const string Alias = "sufni.bridge.ios.preferences";
    private const SecAccessible Accessible = SecAccessible.WhenUnlocked;

    private static SecRecord RecordForKey(string key)
    {
        return new SecRecord(SecKind.GenericPassword)
        {
            Account = key,
            Service = Alias
        };
    }

    private static SecRecord CreateRecord(string key, byte[] value)
    {
        return new SecRecord(SecKind.GenericPassword)
        {
            Account = key,
            Service = Alias,
            Label = key,
            Accessible = Accessible,
            ValueData = NSData.FromArray(value),
        };
    }

    public Task<byte[]?> GetAsync(string key)
    {
        using var record = RecordForKey(key);
        using var match = SecKeyChain.QueryAsRecord(record, out var result);
        return Task.FromResult(result == SecStatusCode.Success ? match!.ValueData!.ToArray() : null);
    }

    public Task<string?> GetStringAsync(string key)
    {
        using var record = RecordForKey(key);
        using var match = SecKeyChain.QueryAsRecord(record, out var result);
        return Task.FromResult<string?>(result == SecStatusCode.Success ? NSString.FromData(match!.ValueData!, NSStringEncoding.UTF8) : null);
    }

    public async Task SetAsync(string key, byte[]? value)
    {
        await RemoveAsync(key);

        if (value is null)
        {
            logger.Verbose("macOS secure storage removed a value");
            return;
        }

        using var record = CreateRecord(key, value);
        var result = SecKeyChain.Add(record);
        if (result != SecStatusCode.Success)
        {
            logger.Error("macOS secure storage add failed with status {Status}", result);
            throw new Exception($"Error adding record: {result}");
        }

        logger.Verbose("macOS secure storage stored a binary value");
    }

    public async Task SetStringAsync(string key, string? value)
    {
        var bytes = value is not null ? Encoding.UTF8.GetBytes(s: value) : null;
        await SetAsync(key, bytes);
    }

    public Task RemoveAsync(string key)
    {
        using var record = RecordForKey(key);
        var result = SecKeyChain.Remove(record);
        if (result != SecStatusCode.Success && result != SecStatusCode.ItemNotFound)
            throw new Exception($"Error removing record: {result}");
        return Task.CompletedTask;
    }

    public Task RemoveAllAsync()
    {
        using var query = new SecRecord(SecKind.GenericPassword);
        query.Service = Alias;
        SecKeyChain.Remove(query);
        logger.Verbose("macOS secure storage removed all values");
        return Task.CompletedTask;
    }
}