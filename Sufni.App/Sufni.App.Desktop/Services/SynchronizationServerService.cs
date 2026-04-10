using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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

public class SynchronizationServerService : ISynchronizationServerService
{
    private const int TokenTtlMinutes = 10;
    private const int RefreshTtlDays = 30;
    private const int Port = 5575;

    private readonly IDatabaseService databaseService;
    private readonly ISecureStorage secureStorage;

    private readonly ConcurrentDictionary<string, (string deviceId, string? displayName, DateTime expiresAt)> pendingPairings = new();

    private static string GeneratePin() => RandomNumberGenerator.GetInt32(100000, 999999).ToString();

    private string? jwtSecret;
    private string? certPassword;

    private readonly string certPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Sufni.App", "certificate.pfx");

    private Task Initialization { get; }

    public event EventHandler<PairingRequestedEventArgs>? PairingRequested;
    public event EventHandler<SynchronizationDataArrivedEventArgs>? SynchronizationDataArrived;
    public event EventHandler<SessionDataArrivedEventArgs>? SessionDataArrived;
    public event EventHandler<PairingEventArgs>? PairingConfirmed;
    public event EventHandler<PairingEventArgs>? Unpaired;

    #region Constructors

    public SynchronizationServerService(IDatabaseService databaseService, ISecureStorage secureStorage)
    {
        this.databaseService = databaseService;
        this.secureStorage = secureStorage;
        Initialization = Init();
    }

    #endregion Constructors

    #region Private methods

    private static void StartAdvertising()
    {
        var service = new ServiceProfile("s1", SynchronizationProtocol.ServiceType, Port);
        var sd = new Makaretu.Dns.ServiceDiscovery();
        if (!sd.Probe(service))
        {
            sd.Advertise(service);
            sd.Announce(service);
        }
    }

    private async Task Init()
    {
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
        var req = new CertificateRequest(SynchronizationProtocol.CertificateSubjectName, ecdsa, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // Server Auth
            critical: false));
        var cert = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(5));
        var pfx = cert.Export(X509ContentType.Pfx, certPassword);
        await File.WriteAllBytesAsync(certPath, pfx);
    }

    private async Task GenerateCertificateIfNeeded()
    {
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

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The synchronization server is a desktop-only feature and these minimal API delegates are explicitly rooted in this method.")]
    public async Task StartAsync()
    {
        var app = await BuildApplication(Port);
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapPost(SynchronizationProtocol.EndpointPairRequest, ([FromBody] PairingRequest req) =>
        {
            var displayName = PairedDevice.NormalizeDisplayName(req.DisplayName);
            var pin = GeneratePin();
            pendingPairings[pin] = (req.DeviceId, displayName, DateTime.UtcNow.AddSeconds(SynchronizationProtocol.PinTtlSeconds));
            PairingRequested?.Invoke(this, new PairingRequestedEventArgs(req.DeviceId, displayName, pin));
            return Results.Ok();
        });

        app.MapPost(SynchronizationProtocol.EndpointPairConfirm, async ([FromBody] PairingConfirm req) =>
        {
            if (!pendingPairings.TryRemove(req.Pin, out var record)) return Results.Unauthorized();
            var displayName = PairedDevice.NormalizeDisplayName(req.DisplayName);
            if (record.deviceId != req.DeviceId ||
                record.displayName != displayName ||
                record.expiresAt < DateTime.UtcNow)
            {
                return Results.Unauthorized();
            }

            var accessToken = GenerateAccessToken(req.DeviceId);
            var pairedDevice = new PairedDevice(req.DeviceId, displayName, DateTime.UtcNow.AddDays(RefreshTtlDays));
            await databaseService.PutPairedDeviceAsync(pairedDevice);

            PairingConfirmed?.Invoke(this, new PairingEventArgs(pairedDevice));
            return Results.Ok(new TokenResponse(accessToken, pairedDevice.Token));
        });

        app.MapPost(SynchronizationProtocol.EndpointPairRefresh, async ([FromBody] RefreshRequest req) =>
        {
            var pairedDevice = await databaseService.GetPairedDeviceByTokenAsync(req.RefreshToken);
            if (pairedDevice is null || pairedDevice.Expires < DateTime.UtcNow) return Results.Unauthorized();

            var newAccessToken = GenerateAccessToken(pairedDevice.DeviceId);
            var newPairedDevice = new PairedDevice(pairedDevice.DeviceId, pairedDevice.DisplayName, DateTime.UtcNow.AddDays(RefreshTtlDays));
            await databaseService.PutPairedDeviceAsync(newPairedDevice);

            return Results.Ok(new TokenResponse(newAccessToken, newPairedDevice.Token));
        });

        app.MapPost(SynchronizationProtocol.EndpointPairUnpair, async ([FromBody] UnpairRequest req) =>
        {
            var device = await databaseService.GetPairedDeviceAsync(req.DeviceId);
            if (device is null) return Results.Ok();
            if (device.Token != req.RefreshToken) return Results.Unauthorized();

            await databaseService.DeletePairedDeviceAsync(device.DeviceId);
            Unpaired?.Invoke(this, new PairingEventArgs(device));

            return Results.Ok();
        });

        app.MapGet(SynchronizationProtocol.EndpointSyncPull, [Authorize] async ([FromQuery] int since, ClaimsPrincipal user) =>
        {
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

        app.MapPut(SynchronizationProtocol.EndpointSyncPush, [Authorize] async ([FromBody] SynchronizationData data, ClaimsPrincipal user) =>
        {
            await databaseService.MergeAllAsync(data);

            SynchronizationDataArrived?.Invoke(this, new SynchronizationDataArrivedEventArgs(data));
            return Results.NoContent();
        });

        app.MapGet(SynchronizationProtocol.EndpointSessionIncomplete, [Authorize] async (ClaimsPrincipal user) =>
        {
            var incompleteSessions = await databaseService.GetIncompleteSessionIdsAsync();
            return Results.Ok(incompleteSessions);
        });

        app.MapGet($"{SynchronizationProtocol.EndpointSessionData}{{id:guid}}", [Authorize] async ([FromRoute] Guid id, ClaimsPrincipal user) =>
        {
            var data = await databaseService.GetSessionPsstAsync(id);
            if (data is null) return Results.NotFound(new { msg = "Session does not exist!" });

            var name = $"{id}.psst";

            return Results.File(
                fileContents: data.BinaryForm,
                contentType: "application/octet-stream",
                fileDownloadName: name
            );
        });

        app.MapPatch($"{SynchronizationProtocol.EndpointSessionData}{{id:guid}}", [Authorize] async ([FromRoute] Guid id, HttpRequest request, ClaimsPrincipal user) =>
        {
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

            SessionDataArrived?.Invoke(this, new SessionDataArrivedEventArgs(id));
            return Results.NoContent();
        });

        StartAdvertising();
        await app.RunAsync();
    }

    #endregion
}