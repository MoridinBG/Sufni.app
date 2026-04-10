using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using DBus.Services.Secrets;
using Sufni.App.Services;

namespace Sufni.App.Linux;

public class LinuxSecureStorage : ISecureStorage
{
    private const string ContentTypeText = "text/plain; charset=utf8";
    private const string ContentTypeBytes = "application/octet-stream";
    private const string LabelPrefix = "Sufni Suspension Telemetry";

    private Collection? defaultCollection;
    private Task Initialization { get; }

    public LinuxSecureStorage()
    {
        Initialization = InitAsync();
    }

    private async Task InitAsync()
    {
        var secretService = await SecretService.ConnectAsync(EncryptionType.Dh);
        defaultCollection = await secretService.GetDefaultCollectionAsync();

        if (defaultCollection is null)
        {
            throw new Exception("Could not access secret storage.");
        }
    }

    private static Dictionary<string, string> GetAttributes(string? key)
    {
        var attributes = new Dictionary<string, string>
        {
            { "service", "sufni-suspension-telemetry" },
        };
        if (key is not null)
        {
            attributes.Add("sufni-suspension-telemetry-api-setting", key);
        }

        return attributes;
    }

    private async Task CreateItemAsync(string key, object? value)
    {
        await Initialization;

        if (value is null)
        {
            await RemoveAsync(key);
            return;
        }

        if (value is not (byte[] or string))
        {
            throw new Exception("Invalid value type!");
        }

        var createdItem = await defaultCollection!.CreateItemAsync(
            $"{LabelPrefix} ({key})",
            GetAttributes(key),
            value as byte[] ?? Encoding.UTF8.GetBytes((value as string)!),
            value is byte[] ? ContentTypeBytes : ContentTypeText,
            true);

        if (createdItem == null)
        {
            throw new Exception($"Could not save {key}");
        }
    }

    private async Task DeleteItemsAsync(string? key)
    {
        await Initialization;

        var attributes = GetAttributes(key);
        var matchedItems = await defaultCollection!.SearchItemsAsync(attributes);
        foreach (var matchedItem in matchedItems)
        {
            Task.Run(async () => await matchedItem.DeleteAsync()).Wait();
        }
    }

    private async Task<byte[]?> SearchItemAsync(string key)
    {
        await Initialization;

        var attributes = GetAttributes(key);
        var matchedItems = await defaultCollection!.SearchItemsAsync(attributes);
        byte[]? secret = null;
        if (matchedItems.Length == 1)
        {
            secret = await matchedItems[0].GetSecretAsync();
        }

        if (matchedItems.Length > 1)
        {
            throw new Exception("Duplicate items!");
        }

        return secret;
    }

    public async Task<byte[]?> GetAsync(string key)
    {
        return await SearchItemAsync(key);
    }

    public async Task<string?> GetStringAsync(string key)
    {
        var valueBytes = await SearchItemAsync(key);
        return valueBytes is not null ? Encoding.UTF8.GetString(valueBytes) : null;
    }

    public async Task SetAsync(string key, byte[]? value)
    {
        await CreateItemAsync(key, value);
    }

    public async Task SetStringAsync(string key, string? value)
    {
        await CreateItemAsync(key, value);
    }

    public async Task RemoveAsync(string key)
    {
        await DeleteItemsAsync(key);
    }

    public async Task RemoveAllAsync()
    {
        await DeleteItemsAsync(null);
    }
}