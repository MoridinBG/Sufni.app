using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using Sufni.App.Infrastructure;

namespace Sufni.App.SyncAndPairing.Services;

internal sealed class SynchronizationTokenService : ISynchronizationTokenService
{
    private readonly Lazy<Task<string>> secret;
    private readonly TimeProvider clock;

    public SynchronizationTokenService(ISecureStorage storage, TimeProvider clock)
    {
        this.clock = clock;
        secret = new Lazy<Task<string>>(async () =>
        {
            var value = await storage.GetStringAsync("jwt_secret");
            if (!string.IsNullOrEmpty(value)) return value;
            value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            await storage.SetStringAsync("jwt_secret", value);
            return value;
        });
    }

    public async Task<byte[]> GetSigningKeyAsync() => Encoding.UTF8.GetBytes(await secret.Value);

    public async Task<string> CreateAccessTokenAsync(string deviceId)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, deviceId)]),
            IssuedAt = now, NotBefore = now, Expires = now.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(await GetSigningKeyAsync()), SecurityAlgorithms.HmacSha256Signature)
        }));
    }
}
