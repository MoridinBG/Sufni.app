using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SecureStorage;
using Sufni.App.Models;
using SyncServer;

namespace Sufni.App.Services;

public class SynchronizationServerService : ISynchronizationServerService
{
    private const int PinTtlSeconds = 60;
    private const int TokenTtlMinutes = 10;
    private const int RefreshTtlDays = 30;

    private readonly IDatabaseService? databaseService = App.Current?.Services?.GetService<IDatabaseService>();
    private readonly ISecureStorage? secureStorage = App.Current?.Services?.GetService<ISecureStorage>();
    
    private readonly ConcurrentDictionary<string, (string deviceId, DateTime expiresAt)> pendingPairings = new();

    private static string GeneratePin() => RandomNumberGenerator.GetInt32(100000, 999999).ToString();

    private string? jwtSecret;
    
    private Task Initialization { get; }

    public SynchronizationServerService()
    {
        Initialization = Init();
    }

    private async Task Init()
    {
        Debug.Assert(secureStorage is not null);
        Debug.Assert(databaseService is not null);

        jwtSecret = await secureStorage.GetStringAsync("jwt_secret");
        if (string.IsNullOrEmpty(jwtSecret))
        {
            jwtSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            await secureStorage.SetStringAsync("jwt_secret", jwtSecret);
        }
    }
    
    private string GenerateAccessToken(string deviceId)
    {
        Debug.Assert(jwtSecret is not null);
        
        var tokenHandler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, deviceId)]),
            Expires = DateTime.UtcNow.AddMinutes(TokenTtlMinutes),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)), 
                SecurityAlgorithms.HmacSha256Signature)
        };
        return tokenHandler.WriteToken(tokenHandler.CreateToken(descriptor));
    }
    
    public async Task StartAsync(int port = 1557)
    {
        await Initialization;
        
        Debug.Assert(jwtSecret is not null);

        var app = ApiHost.BuildApp(jwtSecret);

        app.MapPost("/pair/request", (PairingRequest req) =>
        {
            var pin = GeneratePin();
            Console.WriteLine(pin); // TODO: remove this
            pendingPairings[pin] = (req.DeviceId, DateTime.UtcNow.AddSeconds(PinTtlSeconds));
            return Results.Ok();
        });

        app.MapPost("/pair/confirm", async (PairingConfirm req) =>
        {
            Debug.Assert(databaseService is not null);

            if (!pendingPairings.TryRemove(req.Pin, out var record)) return Results.Unauthorized();
            if (record.deviceId != req.DeviceId || record.expiresAt < DateTime.UtcNow) return Results.Unauthorized();

            var accessToken = GenerateAccessToken(req.DeviceId);
            var refreshToken = new RefreshToken(req.DeviceId, DateTime.UtcNow.AddDays(RefreshTtlDays));
            await databaseService.PutRefreshTokenAsync(refreshToken);

            return Results.Ok(new TokenResponse(accessToken, refreshToken.Token));
        });

        app.MapPost("/pair/refresh", async (RefreshRequest req) =>
        {
            Debug.Assert(databaseService is not null);

            var token = await databaseService.GetRefreshTokenAsync(req.RefreshToken);
            if (token is null || token.Expires < DateTime.UtcNow) return Results.Unauthorized();

            var newAccessToken = GenerateAccessToken(token.DeviceId);
            return Results.Ok(new TokenResponse(newAccessToken, req.RefreshToken)); // or rotate
        });

        app.MapPost("/unpair", (PairingRequest req) =>
        {
            Debug.Assert(databaseService is not null);

            databaseService.DeleteRefreshTokenAsync(req.DeviceId);
        });
        
        app.MapGet("/info", [Authorize] (ClaimsPrincipal user) =>
        {
            var deviceId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Results.Ok($"device identifier: {deviceId}");
        });

        await app.RunAsync();
    }
}