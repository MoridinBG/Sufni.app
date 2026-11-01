using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.SyncAndPairing.Services;

public class SynchronizationTokenServiceTests
{
    [Fact]
    public async Task AccessToken_UsesPersistedSigningKeyDeviceIdentityAndInjectedExpiry()
    {
        var storage = new InMemorySecureStorage();
        const string existingSecret = "persisted-test-secret-with-at-least-thirty-two-characters";
        await storage.SetStringAsync("jwt_secret", existingSecret);
        var clock = PersistenceTestData.CreateTimeProvider();
        var issuer = new SynchronizationTokenService(storage, clock);

        var token = await issuer.CreateAccessTokenAsync("phone");
        var principal = new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(existingSecret)),
            ValidateIssuerSigningKey = true, ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = false
        }, out var parsed);

        Assert.Equal("phone", principal.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddMinutes(10), parsed.ValidTo);
        Assert.Equal(Encoding.UTF8.GetBytes(existingSecret), await issuer.GetSigningKeyAsync());
    }
}
