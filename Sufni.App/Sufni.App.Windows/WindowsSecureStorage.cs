using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Sufni.App.Services;
using SecureStorageDictionary = System.Collections.Concurrent.ConcurrentDictionary<string, byte[]>;

namespace Sufni.App.Windows;

public class WindowsSecureStorage : ISecureStorage
{
    private static readonly byte[] AdditionalEntropy =
    {
        255, 142, 117, 232, 132, 170, 82, 12, 29, 74, 234, 221, 173, 25, 155, 158,
        64, 157, 188, 209, 36, 155, 230, 197, 47, 144, 7, 10, 203, 70, 203, 110,
        209, 74, 44, 90, 211, 206, 106, 218, 54, 154, 173, 56, 195, 182, 88, 171,
        221, 164, 151, 194, 251, 195, 197, 53, 223, 41, 202, 219, 174, 171, 215, 98,
        91, 128, 143, 141, 47, 107, 21, 120, 240, 197, 15, 4, 147, 13, 35, 138,
        159, 119, 18, 221, 199, 13, 145, 136, 243, 27, 34, 220, 127, 253, 38, 47,
        142, 173, 30, 84, 232, 204, 203, 94, 151, 69, 252, 211, 112, 175, 23, 131,
        144, 168, 122, 19, 61, 125, 71, 218, 62, 118, 67, 110, 133, 67, 209, 220,
    };

    private static readonly string AppSecureStoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Sufni.App",
        "preferences.dat");

    private readonly SecureStorageDictionary secureStorage = new();
    private Task Initialization { get; }

    public WindowsSecureStorage()
    {
        Initialization = InitAsync();
    }

    private async Task InitAsync()
    {
        if (!File.Exists(AppSecureStoragePath))
            return;

        try
        {
            await using var stream = File.OpenRead(AppSecureStoragePath);
            var readPreferences = await JsonSerializer.DeserializeAsync<SecureStorageDictionary>(stream);

            if (readPreferences != null)
            {
                secureStorage.Clear();
                foreach (var pair in readPreferences)
                {
                    secureStorage.TryAdd(pair.Key, pair.Value);
                }
            }
        }
        catch (JsonException)
        {
            // if deserialization fails proceed with empty settings
        }
    }

    private async Task SaveAsync()
    {
        await Initialization;

        var dir = Path.GetDirectoryName(AppSecureStoragePath);
        Debug.Assert(dir != null, nameof(dir) + " != null");
        Directory.CreateDirectory(dir);

        await using var stream = File.Create(AppSecureStoragePath);
        await JsonSerializer.SerializeAsync(stream, secureStorage);
    }

    public async Task<byte[]?> GetAsync(string key)
    {
        await Initialization;

        if (!secureStorage.TryGetValue(key, out var value))
        {
            return null;
        }

        return ProtectedData.Unprotect(
            value,
            AdditionalEntropy,
            DataProtectionScope.CurrentUser);
    }

    public async Task<string?> GetStringAsync(string key)
    {
        await Initialization;

        if (!secureStorage.TryGetValue(key, out var value))
        {
            return null;
        }

        var data = ProtectedData.Unprotect(
            value, AdditionalEntropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(data);
    }

    public async Task SetAsync(string key, byte[]? value)
    {
        await Initialization;

        if (value is null)
        {
            secureStorage.TryRemove(key, out _);
        }
        else
        {
            secureStorage[key] = ProtectedData.Protect(
                value, AdditionalEntropy, DataProtectionScope.CurrentUser);
        }

        await SaveAsync();
    }

    public async Task SetStringAsync(string key, string? value)
    {
        await Initialization;

        if (value is null)
        {
            secureStorage.TryRemove(key, out _);
        }
        else
        {
            var data = Encoding.UTF8.GetBytes(s: value);
            secureStorage[key] = ProtectedData.Protect(
                data, AdditionalEntropy, DataProtectionScope.CurrentUser);
        }

        await SaveAsync();
    }

    public async Task RemoveAsync(string key)
    {
        await Initialization;

        secureStorage.TryRemove(key, out _);
        await SaveAsync();
    }

    public async Task RemoveAllAsync()
    {
        await Initialization;

        secureStorage.Clear();
        await SaveAsync();
    }
}