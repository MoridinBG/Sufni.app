using System;
using System.Text;
using System.Threading.Tasks;
using Foundation;
using Security;
using Serilog;
using Sufni.App.Services;

namespace Sufni.App.AppleShared;

public abstract class AppleSecureStorageBase : ISecureStorage
{
    private const SecAccessible Accessible = SecAccessible.WhenUnlocked;

    private readonly ILogger logger;
    private readonly string platformName;
    private readonly string alias;

    protected AppleSecureStorageBase(string platformName, string alias)
    {
        this.platformName = platformName;
        this.alias = alias;
        logger = Log.ForContext(GetType());
    }

    public Task<byte[]?> GetAsync(string key) => Task.FromResult(QueryValue(key));

    public Task<string?> GetStringAsync(string key)
    {
        var value = QueryValue(key);
        return Task.FromResult(value is null ? null : Encoding.UTF8.GetString(value));
    }

    public async Task SetAsync(string key, byte[]? value)
    {
        await RemoveAsync(key);

        if (value is null)
        {
            logger.Verbose("{Platform} secure storage removed a value", platformName);
            return;
        }

        using var record = CreateRecord(alias, key, value);
        var result = SecKeyChain.Add(record);
        if (result != SecStatusCode.Success)
        {
            logger.Error("{Platform} secure storage add failed with status {Status}", platformName, result);
            throw new Exception($"Error adding record: {result}");
        }

        logger.Verbose("{Platform} secure storage stored a binary value", platformName);
    }

    public async Task SetStringAsync(string key, string? value)
    {
        var bytes = value is null ? null : Encoding.UTF8.GetBytes(value);
        await SetAsync(key, bytes);
    }

    public Task RemoveAsync(string key)
    {
        RemoveRecord(alias, key);

        return Task.CompletedTask;
    }

    public Task RemoveAllAsync()
    {
        RemoveAllRecords(alias);

        logger.Verbose("{Platform} secure storage removed all values", platformName);
        return Task.CompletedTask;
    }

    private byte[]? QueryValue(string key)
    {
        if (TryQueryValue(alias, key, out var value))
        {
            return value;
        }

        return null;
    }

    private static bool TryQueryValue(string serviceAlias, string key, out byte[]? value)
    {
        using var record = RecordForKey(serviceAlias, key);
        using var match = SecKeyChain.QueryAsRecord(record, out var result);
        if (result == SecStatusCode.Success)
        {
            value = match!.ValueData!.ToArray();
            return true;
        }

        value = null;
        return false;
    }

    private static void RemoveRecord(string serviceAlias, string key)
    {
        using var record = RecordForKey(serviceAlias, key);
        var result = SecKeyChain.Remove(record);
        if (result != SecStatusCode.Success && result != SecStatusCode.ItemNotFound)
        {
            throw new Exception($"Error removing record: {result}");
        }
    }

    private static void RemoveAllRecords(string serviceAlias)
    {
        using var query = new SecRecord(SecKind.GenericPassword)
        {
            Service = serviceAlias
        };
        SecKeyChain.Remove(query);
    }

    private static SecRecord RecordForKey(string serviceAlias, string key)
    {
        return new SecRecord(SecKind.GenericPassword)
        {
            Account = key,
            Service = serviceAlias
        };
    }

    private static SecRecord CreateRecord(string serviceAlias, string key, byte[] value)
    {
        return new SecRecord(SecKind.GenericPassword)
        {
            Account = key,
            Service = serviceAlias,
            Label = key,
            Accessible = Accessible,
            ValueData = NSData.FromArray(value),
        };
    }
}