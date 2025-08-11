using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SecureStorage;
using Sufni.App.Models;

namespace Sufni.App.Services;

public class SynchronizationServerService : ISynchronizationServerService
{
    public const int PinTtlSeconds = 30;
    private const int TokenTtlMinutes = 10;
    private const int RefreshTtlDays = 30;
    private const int Port = 5575;

    private readonly IDatabaseService? databaseService = App.Current?.Services?.GetService<IDatabaseService>();
    private readonly ISecureStorage? secureStorage = App.Current?.Services?.GetService<ISecureStorage>();
    
    private readonly ConcurrentDictionary<string, (string deviceId, DateTime expiresAt)> pendingPairings = new();

    private static string GeneratePin() => RandomNumberGenerator.GetInt32(100000, 999999).ToString();

    private string? jwtSecret;
    
    private Task Initialization { get; }
    
    public Action<string, string>? PairingPinCallback { get; set; }

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

    private async Task<WebApplication> BuildApplication(int port)
    {
        await Initialization;
        
        Debug.Assert(jwtSecret is not null);

        var builder = WebApplication.CreateBuilder();
        
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(port);
        });
        
        var key = Encoding.ASCII.GetBytes(jwtSecret);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                RequireExpirationTime = true,
                ValidateLifetime = true
            };
        });
        builder.Services.AddAuthorization();

        return builder.Build();
    }
    
    public async Task StartAsync()
    {
        var app =  await BuildApplication(Port);
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapPost("/pair/request", ([FromBody] PairingRequest req) =>
        {
            if (PairingPinCallback is null) return Results.InternalServerError();

            var pin = GeneratePin();
            PairingPinCallback(req.DeviceId, pin);
            pendingPairings[pin] = (req.DeviceId, DateTime.UtcNow.AddSeconds(PinTtlSeconds));
            return Results.Ok();
        });

        app.MapPost("/pair/confirm", async ([FromBody] PairingConfirm req) =>
        {
            Debug.Assert(databaseService is not null);

            if (!pendingPairings.TryRemove(req.Pin, out var record)) return Results.Unauthorized();
            if (record.deviceId != req.DeviceId || record.expiresAt < DateTime.UtcNow) return Results.Unauthorized();

            var accessToken = GenerateAccessToken(req.DeviceId);
            var pairedDevice = new PairedDevice(req.DeviceId, DateTime.UtcNow.AddDays(RefreshTtlDays));
            await databaseService.PutPairedDeviceAsync(pairedDevice);

            return Results.Ok(new TokenResponse(accessToken, pairedDevice.Token));
        });

        app.MapPost("/pair/refresh", async ([FromBody] RefreshRequest req) =>
        {
            Debug.Assert(databaseService is not null);

            var pairedDevice = await databaseService.GetPairedDeviceAsync(req.RefreshToken);
            if (pairedDevice is null || pairedDevice.Expires < DateTime.UtcNow) return Results.Unauthorized();

            var newAccessToken = GenerateAccessToken(pairedDevice.DeviceId);
            return Results.Ok(new TokenResponse(newAccessToken, req.RefreshToken)); // TODO: rotate refresh token
        });

        app.MapDelete("/pair/unpair", [Authorize] async (ClaimsPrincipal user) =>
        {
            Debug.Assert(databaseService is not null);

            var deviceId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (deviceId is null) return Results.NotFound();

            await databaseService.DeletePairedDeviceAsync(deviceId);
            
            return Results.Ok();
        });

        app.MapGet("/sync/pull", [Authorize] async ([FromQuery] int since, ClaimsPrincipal user) =>
        {
            Debug.Assert(databaseService is not null);

            var data = new SynchronizationData
            {
                Boards = await databaseService.GetChangedBoardsAsync(since),
                Bikes = await databaseService.GetChangedBikesAsync(since),
                Setups = await databaseService.GetChangedSetupsAsync(since),
                Sessions = await databaseService.GetChangedSessionsAsync(since)
            };

            return Results.Ok(data);
        });

        app.MapPut("/sync/push", [Authorize] async ([FromBody] SynchronizationData data, ClaimsPrincipal user) =>
        {
            Debug.Assert(databaseService is not null);

            await databaseService.MergeAllAsync(data);
        });

        app.MapGet("/session/incomplete", [Authorize] async (ClaimsPrincipal user) =>
        {
            Debug.Assert(databaseService is not null);

            var incompleteSessions = await databaseService.GetIncompleteSessionIdsAsync();
            return Results.Ok(incompleteSessions);
        });

        app.MapGet("/session/{id:guid}/psst", [Authorize] async ([FromRoute] Guid id, ClaimsPrincipal user) =>
        {
            Debug.Assert(databaseService is not null);

            var data = await databaseService.GetSessionPsstAsync(id);
            if  (data is null) return Results.NotFound(new { msg = "Session does not exist!" });

            var name = $"{id}.psst";

            return Results.File(
                fileContents: data.BinaryForm,
                contentType: "application/octet-stream",
                fileDownloadName: name
            );
        });

        app.MapPatch("/session/{id:guid}/psst", [Authorize] async ([FromRoute] Guid id, HttpRequest request, ClaimsPrincipal user) =>
        {
            Debug.Assert(databaseService is not null);

            await using var memoryStream = new MemoryStream();
            await request.BodyReader.CopyToAsync(memoryStream);
            var data = memoryStream.ToArray();

            try
            {
                await databaseService.PatchSessionPsstAsync(id, data);
            }
            catch (Exception)
            {
                return Results.NotFound();
            }

            return Results.NoContent();
        });

        await app.RunAsync();
    }
}