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
using System.Text.Json;
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
using Serilog;
using Sufni.App.Models;

namespace Sufni.App.Services;

public class SynchronizationServerService : ISynchronizationServerService
{
    private static readonly ILogger logger = Log.ForContext<SynchronizationServerService>();

    private const int TokenTtlMinutes = 10;
    private const int RefreshTtlDays = 30;
    private const int Port = 5575;

    private readonly IDatabaseService databaseService;
    private readonly ISecureStorage secureStorage;
    private readonly object advertisingGate = new();

    private readonly ConcurrentDictionary<string, (string deviceId, string? displayName, DateTime expiresAt)> pendingPairings = new();

    private static string GeneratePin() => RandomNumberGenerator.GetInt32(100000, 999999).ToString();

    private string? jwtSecret;
    private string? certPassword;
    private Makaretu.Dns.ServiceDiscovery? serviceDiscovery;
    private ServiceProfile? advertisedService;

    private readonly string certPath = AppPaths.CertificatePath;

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

    private void StartAdvertising()
    {
        lock (advertisingGate)
        {
            serviceDiscovery?.Dispose();
            serviceDiscovery = null;
            advertisedService = null;

            var service = new ServiceProfile("s1", SynchronizationProtocol.ServiceType, Port);
            var discovery = new Makaretu.Dns.ServiceDiscovery();
            if (discovery.Probe(service))
            {
                discovery.Dispose();
                return;
            }

            discovery.Advertise(service);
            discovery.Announce(service);
            advertisedService = service;
            serviceDiscovery = discovery;
        }
    }

    private void StopAdvertising()
    {
        lock (advertisingGate)
        {
            serviceDiscovery?.Dispose();
            serviceDiscovery = null;
            advertisedService = null;
        }
    }

    private async Task Init()
    {
        jwtSecret = await secureStorage.GetStringAsync("jwt_secret");
        if (string.IsNullOrEmpty(jwtSecret))
        {
            jwtSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            await secureStorage.SetStringAsync("jwt_secret", jwtSecret);
            logger.Verbose("Generated new synchronization JWT secret");
        }

        await GenerateCertificateIfNeeded();
    }

    private async Task GenerateCertificate()
    {
        AppPaths.CreateRequiredDirectories();
        logger.Verbose("Generating synchronization server certificate at {CertificatePath}", certPath);

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
            logger.Verbose("Synchronization certificate missing or password unavailable; generating a new certificate");
            await GenerateCertificate();
            return;
        }

        // Check if the certificate has not expired.
        var cert = X509CertificateLoader.LoadPkcs12FromFile(certPath, certPassword);
        if (cert.NotAfter < DateTimeOffset.Now)
        {
            logger.Verbose("Synchronization certificate expired at {CertificateExpiry}; generating a new certificate", cert.NotAfter);
            await GenerateCertificate();
        }
    }

    internal static byte[] GetJwtSecretKeyBytes(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return Encoding.UTF8.GetBytes(secret);
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
                new SymmetricSecurityKey(GetJwtSecretKeyBytes(jwtSecret)),
                SecurityAlgorithms.HmacSha256Signature)
        };
        return tokenHandler.WriteToken(tokenHandler.CreateToken(descriptor));
    }

    private async Task<WebApplication> BuildApplication(int port)
    {
        await Initialization;

        Debug.Assert(jwtSecret is not null);

        logger.Verbose("Building synchronization server pipeline for port {Port}", port);

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

        var key = GetJwtSecretKeyBytes(jwtSecret);
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
        try
        {
            logger.Information("Starting synchronization server on port {Port}", Port);

            var app = await BuildApplication(Port);
            app.Lifetime.ApplicationStopping.Register(StopAdvertising);
            app.Lifetime.ApplicationStopped.Register(StopAdvertising);
            app.UseAuthentication();
            app.UseAuthorization();
            app.Use(async (context, next) =>
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await next();
                }
                finally
                {
                    logger.Verbose(
                        "Synchronization server handled {Method} {Path} with status {StatusCode} in {DurationMs} ms",
                        context.Request.Method,
                        context.Request.Path.Value ?? string.Empty,
                        context.Response.StatusCode,
                        stopwatch.Elapsed.TotalMilliseconds);
                }
            });

            app.MapPost(SynchronizationProtocol.EndpointPairRequest, ([FromBody] PairingRequest req) =>
            {
                logger.Verbose("Pairing request received for {DeviceId}", req.DeviceId);

                var displayName = PairedDevice.NormalizeDisplayName(req.DisplayName);
                var pin = GeneratePin();
                pendingPairings[pin] = (req.DeviceId, displayName, DateTime.UtcNow.AddSeconds(SynchronizationProtocol.PinTtlSeconds));
                PairingRequested?.Invoke(this, new PairingRequestedEventArgs(req.DeviceId, displayName, pin));
                return Results.Ok();
            });

            app.MapPost(SynchronizationProtocol.EndpointPairConfirm, async ([FromBody] PairingConfirm req) =>
            {
                if (!pendingPairings.TryRemove(req.Pin, out var record))
                {
                    logger.Warning("Pairing confirmation rejected for {DeviceId}: PIN not found", req.DeviceId);
                    return Results.Unauthorized();
                }

                var displayName = PairedDevice.NormalizeDisplayName(req.DisplayName);
                if (record.deviceId != req.DeviceId ||
                    record.displayName != displayName ||
                    record.expiresAt < DateTime.UtcNow)
                {
                    logger.Warning("Pairing confirmation rejected for {DeviceId}: record mismatch or expired PIN", req.DeviceId);
                    return Results.Unauthorized();
                }

                var accessToken = GenerateAccessToken(req.DeviceId);
                var pairedDevice = new PairedDevice(req.DeviceId, displayName, DateTime.UtcNow.AddDays(RefreshTtlDays));
                await databaseService.PutPairedDeviceAsync(pairedDevice);

                logger.Verbose("Pairing confirmed for {DeviceId}", req.DeviceId);
                PairingConfirmed?.Invoke(this, new PairingEventArgs(pairedDevice));
                return Results.Ok(new TokenResponse(accessToken, pairedDevice.Token));
            });

            app.MapPost(SynchronizationProtocol.EndpointPairRefresh, async ([FromBody] RefreshRequest req) =>
            {
                var pairedDevice = await databaseService.GetPairedDeviceByTokenAsync(req.RefreshToken);
                if (pairedDevice is null || pairedDevice.Expires < DateTime.UtcNow)
                {
                    logger.Warning("Token refresh rejected because the paired device was missing or expired");
                    return Results.Unauthorized();
                }

                var newAccessToken = GenerateAccessToken(pairedDevice.DeviceId);
                var newPairedDevice = new PairedDevice(pairedDevice.DeviceId, pairedDevice.DisplayName, DateTime.UtcNow.AddDays(RefreshTtlDays));
                await databaseService.PutPairedDeviceAsync(newPairedDevice);

                logger.Verbose("Issued refreshed synchronization token for {DeviceId}", pairedDevice.DeviceId);
                return Results.Ok(new TokenResponse(newAccessToken, newPairedDevice.Token));
            });

            app.MapPost(SynchronizationProtocol.EndpointPairUnpair, async ([FromBody] UnpairRequest req) =>
            {
                var device = await databaseService.GetPairedDeviceAsync(req.DeviceId);
                if (device is null)
                {
                    logger.Verbose("Ignoring unpair request for {DeviceId} because no device was found", req.DeviceId);
                    return Results.Ok();
                }

                if (device.Token != req.RefreshToken)
                {
                    logger.Warning("Unpair request rejected for {DeviceId}: refresh token mismatch", req.DeviceId);
                    return Results.Unauthorized();
                }

                await databaseService.DeletePairedDeviceAsync(device.DeviceId);
                logger.Verbose("Unpaired device {DeviceId}", device.DeviceId);
                Unpaired?.Invoke(this, new PairingEventArgs(device));

                return Results.Ok();
            });

            app.MapGet(SynchronizationProtocol.EndpointSyncPull, [Authorize] async ([FromQuery] long since, ClaimsPrincipal user) =>
            {
                var data = await databaseService.GetSynchronizationDataAsync(since);

                logger.Verbose(
                    "Synchronization pull since {Since} returned {BoardCount} boards, {BikeCount} bikes, {SetupCount} setups, {SessionCount} sessions, and {TrackCount} tracks",
                    since,
                    data.Boards.Count,
                    data.Bikes.Count,
                    data.Setups.Count,
                    data.Sessions.Count,
                    data.Tracks.Count);

                return Results.Ok(data);
            });

            app.MapPut(SynchronizationProtocol.EndpointSyncPush, [Authorize] async ([FromBody] SynchronizationData data, ClaimsPrincipal user) =>
            {
                logger.Verbose(
                    "Synchronization push received with {BoardCount} boards, {BikeCount} bikes, {SetupCount} setups, {SessionCount} sessions, and {TrackCount} tracks",
                    data.Boards.Count,
                    data.Bikes.Count,
                    data.Setups.Count,
                    data.Sessions.Count,
                    data.Tracks.Count);

                await databaseService.MergeAllAsync(CloneSynchronizationData(data));

                SynchronizationDataArrived?.Invoke(this, new SynchronizationDataArrivedEventArgs(data));
                return Results.NoContent();
            });

            app.MapGet(SynchronizationProtocol.EndpointSessionIncomplete, [Authorize] async (ClaimsPrincipal user) =>
            {
                var incompleteSessions = await databaseService.GetIncompleteSessionIdsAsync();
                logger.Verbose("Synchronization incomplete-session query returned {SessionCount} sessions", incompleteSessions.Count);
                return Results.Ok(incompleteSessions);
            });

            app.MapGet($"{SynchronizationProtocol.EndpointSessionData}{{id:guid}}", [Authorize] async ([FromRoute] Guid id, ClaimsPrincipal user) =>
            {
                var data = await databaseService.GetSessionRawPsstAsync(id);
                if (data is null)
                {
                    logger.Warning("Session data download failed because session {SessionId} was not found", id);
                    return Results.NotFound(new { msg = "Session does not exist!" });
                }

                logger.Verbose("Serving session data for {SessionId} with {ByteCount} bytes", id, data.Length);
                var name = $"{id}.psst";

                return Results.File(
                    fileContents: data,
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
                catch (Exception ex)
                {
                    logger.Error(ex, "Session data patch failed because session {SessionId} was not found", id);
                    return Results.NotFound();
                }

                logger.Verbose("Patched session data for {SessionId} with {ByteCount} bytes", id, data.Length);
                SessionDataArrived?.Invoke(this, new SessionDataArrivedEventArgs(id));
                return Results.NoContent();
            });

            logger.Verbose("Advertising synchronization service on port {Port}", Port);
            StartAdvertising();
            logger.Information("Synchronization server listening on port {Port}", Port);
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            StopAdvertising();
            logger.Error(ex, "Synchronization server failed to start or run on port {Port}", Port);
            throw;
        }
    }

    private static SynchronizationData CloneSynchronizationData(SynchronizationData data)
    {
        var clone = JsonSerializer.Deserialize<SynchronizationData>(JsonSerializer.Serialize(data));
        return clone ?? throw new InvalidOperationException("Failed to clone synchronization payload.");
    }

    #endregion
}