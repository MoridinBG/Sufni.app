using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Net;
using System.Security.Authentication;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Makaretu.Dns;
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

public class PairingEventArgs(PairedDevice device) : EventArgs
{
    public PairedDevice Device { get; set; } = device;
}

public class SynchronizationServerService : ISynchronizationServerService
{
    public const string ServiceType = "_sstsync._tcp";
    public const string CertificateSubjectName = "cn=com.sghctoma.sst-api";
    public const int PinTtlSeconds = 30;
    public const string EndpointPairRequest = "/pair/request";
    public const string EndpointPairConfirm = "/pair/confirm";
    public const string EndpointPairRefresh = "/pair/refresh";
    public const string EndpointPairUnpair = "/pair/unpair";
    public const string EndpointSyncPush = "/sync/push";
    public const string EndpointSyncPull = "/sync/pull";
    public const string EndpointSessionIncomplete = "/session/incomplete";
    public const string EndpointSessionData = "/session/data/";

    private const int TokenTtlMinutes = 10;
    private const int RefreshTtlDays = 30;
    private const int Port = 5575;

    private readonly IDatabaseService? databaseService = App.Current?.Services?.GetService<IDatabaseService>();
    private readonly ISecureStorage? secureStorage = App.Current?.Services?.GetService<ISecureStorage>();
    
    private readonly ConcurrentDictionary<string, (string deviceId, DateTime expiresAt)> pendingPairings = new();

    private static string GeneratePin() => RandomNumberGenerator.GetInt32(100000, 999999).ToString();

    private string? jwtSecret;
    private string? certPassword;

    private readonly string certPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Sufni.App", "certificate.pfx");
    
    private Task Initialization { get; }
    
    public Action<string, string>? PairingRequested { get; set; }
    public Action<SynchronizationData>? SynchronizationDataArrived { get; set; }
    public Action<Guid>? SessionDataArrived { get; set; }

    public event EventHandler? PairingConfirmed;
    public event EventHandler? Unpaired;

    #region Constructors

    public SynchronizationServerService()
    {
        Initialization = Init();
    }

    #endregion Constructors

    #region Private methods

    private static void StartAdvertising()
    {
        var service = new ServiceProfile("s1", ServiceType, Port);
        var sd = new Makaretu.Dns.ServiceDiscovery();
        if (!sd.Probe(service))
        {
            sd.Advertise(service);
            sd.Announce(service);
        }
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

        await GenerateCertificateIfNeeded();
    }

    private async Task GenerateCertificate()
    {
        var dir = Path.GetDirectoryName(certPath)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest(CertificateSubjectName, ecdsa, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // Server Auth
            critical: false));
        var cert = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(5));
        var pfx = cert.Export(X509ContentType.Pfx,  certPassword);
        await File.WriteAllBytesAsync(certPath, pfx);
    }

    private async Task GenerateCertificateIfNeeded()
    {
        Debug.Assert(secureStorage is not null);

        certPassword = await secureStorage.GetStringAsync("cert_password");

        // If there was no stored certificate password, or the certificate file is missing, we generate
        // a new password and a new certificate.
        if (string.IsNullOrEmpty(certPassword) || !File.Exists(certPath))
        {
            certPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            await secureStorage.SetStringAsync("cert_password", certPassword);
            await GenerateCertificate();
            return;
        }
        
        // Check if the certificate has not expired.
        var cert = X509CertificateLoader.LoadPkcs12FromFile(certPath, certPassword);
        if (cert.NotAfter < DateTimeOffset.Now)
        {
            await GenerateCertificate();
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
            options.ConfigureHttpsDefaults(httpsOptions =>
            {
                httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
            });
            options.Listen(IPAddress.IPv6Any, port, listenOptions =>
            {
                listenOptions.UseHttps(certPath, certPassword);
            });
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

    #endregion Private methods

    #region Public methods

    public async Task StartAsync()
    {
        var app =  await BuildApplication(Port);
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapPost(EndpointPairRequest, ([FromBody] PairingRequest req) =>
        {
            var pin = GeneratePin();
            pendingPairings[pin] = (req.DeviceId, DateTime.UtcNow.AddSeconds(PinTtlSeconds));
            PairingRequested?.Invoke(req.DeviceId, pin);
            return Results.Ok();
        });

        app.MapPost(EndpointPairConfirm, async ([FromBody] PairingConfirm req) =>
        {
            Debug.Assert(databaseService is not null);

            if (!pendingPairings.TryRemove(req.Pin, out var record)) return Results.Unauthorized();
            if (record.deviceId != req.DeviceId || record.expiresAt < DateTime.UtcNow) return Results.Unauthorized();

            var accessToken = GenerateAccessToken(req.DeviceId);
            var pairedDevice = new PairedDevice(req.DeviceId, DateTime.UtcNow.AddDays(RefreshTtlDays));
            await databaseService.PutPairedDeviceAsync(pairedDevice);

            PairingConfirmed?.Invoke(this, new PairingEventArgs(pairedDevice));
            return Results.Ok(new TokenResponse(accessToken, pairedDevice.Token));
        });

        app.MapPost(EndpointPairRefresh, async ([FromBody] RefreshRequest req) =>
        {
            Debug.Assert(databaseService is not null);

            var pairedDevice = await databaseService.GetPairedDeviceAsync(req.RefreshToken);
            if (pairedDevice is null || pairedDevice.Expires < DateTime.UtcNow) return Results.Unauthorized();

            var newAccessToken = GenerateAccessToken(pairedDevice.DeviceId);
            var newPairedDevice = new PairedDevice(pairedDevice.DeviceId, DateTime.UtcNow.AddDays(RefreshTtlDays));
            await databaseService.PutPairedDeviceAsync(newPairedDevice);

            return Results.Ok(new TokenResponse(newAccessToken, newPairedDevice.Token));
        });

        app.MapPost(EndpointPairUnpair, async ([FromBody] UnpairRequest req) =>
        {
            Debug.Assert(databaseService is not null);

            var device = await databaseService.GetPairedDeviceAsync(req.DeviceId);
            if (device is null) return Results.Ok();
            if (device.Token != req.RefreshToken) return Results.Unauthorized();

            await databaseService.DeletePairedDeviceAsync(device.DeviceId);
            Unpaired?.Invoke(this, new PairingEventArgs(device));
            
            return Results.Ok();
        });

        app.MapGet(EndpointSyncPull, [Authorize] async ([FromQuery] int since, ClaimsPrincipal user) =>
        {
            Debug.Assert(databaseService is not null);

            var data = new SynchronizationData
            {
                Boards = await databaseService.GetChangedAsync<Board>(since),
                Bikes = await databaseService.GetChangedAsync<Bike>(since),
                Setups = await databaseService.GetChangedAsync<Setup>(since),
                Sessions = await databaseService.GetChangedAsync<Session>(since),
                Tracks = await databaseService.GetChangedAsync<Track>(since)
            };

            return Results.Ok(data);
        });

        app.MapPut(EndpointSyncPush, [Authorize] async ([FromBody] SynchronizationData data, ClaimsPrincipal user) =>
        {
            Debug.Assert(databaseService is not null);

            await databaseService.MergeAllAsync(data);

            SynchronizationDataArrived?.Invoke(data);
            return Results.NoContent();
        });

        app.MapGet(EndpointSessionIncomplete, [Authorize] async (ClaimsPrincipal user) =>
        {
            Debug.Assert(databaseService is not null);

            var incompleteSessions = await databaseService.GetIncompleteSessionIdsAsync();
            return Results.Ok(incompleteSessions);
        });

        app.MapGet($"{EndpointSessionData}{{id:guid}}", [Authorize] async ([FromRoute] Guid id, ClaimsPrincipal user) =>
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

        app.MapPatch($"{EndpointSessionData}{{id:guid}}", [Authorize] async ([FromRoute] Guid id, HttpRequest request, ClaimsPrincipal user) =>
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

            SessionDataArrived?.Invoke(id);
            return Results.NoContent();
        });

        StartAdvertising();
        await app.RunAsync();
    }

    #endregion
}