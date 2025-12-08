using System.Diagnostics;
using Android.Content;
using Android.Runtime;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace SecureStorage;

public class SecureStorageException : Exception;

public class SecureStorage : ISecureStorage
{
    private const string KeyAlias = "sufni.app.android.keystore.aes";
    private const string PrefName = "sufni.app.prefs";

    private readonly ISharedPreferences? prefs;

    public SecureStorage()
    {
        var context = Application.Context;
        prefs = context.GetSharedPreferences(PrefName, FileCreationMode.Private);
        EnsureKey();
    }

    private static void EnsureKey()
    {
        var keyStore = KeyStore.GetInstance("AndroidKeyStore");
        Debug.Assert(keyStore is not null);
        keyStore.Load(null);

        if (keyStore.ContainsAlias(KeyAlias)) return;
        var keyGenerator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore");
        Debug.Assert(keyGenerator is not null);

        var builder = new KeyGenParameterSpec.Builder(
                KeyAlias,
                KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetRandomizedEncryptionRequired(true);

        keyGenerator.Init(builder.Build());
        keyGenerator.GenerateKey();
    }

    private static ISecretKey? GetSecretKey()
    {
        var keyStore = KeyStore.GetInstance("AndroidKeyStore");

        keyStore?.Load(null);
        var key = keyStore?.GetKey(KeyAlias, null);
        return key.JavaCast<ISecretKey>();
    }

    private static (byte[]? iv, byte[]? ciphertext) Encrypt(byte[] plain)
    {
        var cipher = Cipher.GetInstance("AES/GCM/NoPadding");

        cipher?.Init(CipherMode.EncryptMode, GetSecretKey());

        var iv = cipher?.GetIV();
        var ciphertext = cipher?.DoFinal(plain);
        return (iv, ciphertext);
    }

    private static byte[]? Decrypt(byte[] iv, byte[] ciphertext)
    {
        try
        {
            var spec = new GCMParameterSpec(128, iv);
            var cipher = Cipher.GetInstance("AES/GCM/NoPadding");

            cipher?.Init(CipherMode.DecryptMode, GetSecretKey(), spec);
            return cipher?.DoFinal(ciphertext);
        }
        catch
        {
            return null;
        }
    }

    public Task<byte[]?> GetAsync(string key)
    {
        var ivB64 = prefs?.GetString(key + "_iv", null);
        var ctB64 = prefs?.GetString(key + "_ct", null);
        if (ivB64 == null || ctB64 == null)
            return Task.FromResult<byte[]?>(null);

        var iv = Convert.FromBase64String(ivB64);
        var ct = Convert.FromBase64String(ctB64);
        return Task.FromResult(Decrypt(iv, ct));
    }

    public async Task<string?> GetStringAsync(string key)
    {
        var b = await GetAsync(key);
        return b == null ? null : System.Text.Encoding.UTF8.GetString(b);
    }

    public Task SetAsync(string key, byte[]? value)
    {
        var editor = prefs?.Edit();

        if (value == null)
        {
            editor?.Remove(key + "_iv");
            editor?.Remove(key + "_ct");
        }
        else
        {
            var (iv, ct) = Encrypt(value);
            if (iv is null || ct == null) return Task.FromException(new SecureStorageException());
            editor?.PutString(key + "_iv", Convert.ToBase64String(iv));
            editor?.PutString(key + "_ct", Convert.ToBase64String(ct));
        }

        editor?.Apply();
        return Task.CompletedTask;
    }

    public Task SetStringAsync(string key, string? value)
    {
        return SetAsync(key, value == null ? null : System.Text.Encoding.UTF8.GetBytes(value));
    }

    public Task RemoveAsync(string key)
    {
        var editor = prefs?.Edit();
        editor?.Remove(key + "_iv");
        editor?.Remove(key + "_ct");
        editor?.Apply();
        return Task.CompletedTask;
    }

    public Task RemoveAllAsync()
    {
        var editor = prefs?.Edit();
        editor?.Clear();
        editor?.Apply();
        return Task.CompletedTask;
    }
}
