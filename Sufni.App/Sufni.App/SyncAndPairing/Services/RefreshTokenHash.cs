using System;
using System.Security.Cryptography;
using System.Text;

namespace Sufni.App.SyncAndPairing.Services;

internal static class RefreshTokenHash
{
    public static string Compute(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
