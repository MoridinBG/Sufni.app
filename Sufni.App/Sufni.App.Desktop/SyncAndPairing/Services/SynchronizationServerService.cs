using Sufni.App.Shared.Models;
using System;
using Microsoft.Extensions.Logging;
using ILogger = Serilog.ILogger;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Makaretu.Dns;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Serilog;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Coordinators;
namespace Sufni.App.SyncAndPairing.Services;

public class SynchronizationServerService : ISynchronizationServerService
{
    private static readonly ILogger logger = Log.ForContext<SynchronizationServerService>();

    private const int Port = 5575;
    private const string DefaultServiceInstanceName = "s1";
    private const int MaxServiceProbeAttempts = 5;
    private const long MaxSyncRequestBodyBytes = 256L * 1024 * 1024;

    private readonly IInboundSyncCoordinator inboundSyncCoordinator;
    private readonly ISynchronizationTokenService tokens;
    private readonly ISessionRepository sessionRepository;
    private readonly IRecordedSessionSourceRepository recordedSessionSourceRepository;
    private readonly IRecordedSessionSourceSyncQuery recordedSessionSourceSyncQuery;
    private readonly ISessionBlobSwapRequestStore swapRequestStore;
    private readonly ISecureStorage secureStorage;
    private readonly System.Threading.Lock advertisingGate = new();
    private readonly System.Threading.Lock startGate = new();

    private static async ValueTask<object?> RequireSyncProtocolVersionAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var expected = SynchronizationProtocol.SyncProtocolVersion.ToString(CultureInfo.InvariantCulture);
        var actual = context.HttpContext.Request.Headers[SynchronizationProtocol.SyncProtocolHeader].ToString();
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status426UpgradeRequired,
                title: "Sync protocol version mismatch");
        }

        return await next(context);
    }

    private sealed record RecordedSourceRequestMetadata(
        RecordedSessionSourceKind SourceKind,
        string SourceName,
        int SchemaVersion,
        string SourceHash);

    private static bool IsOctetStreamRequest(HttpRequest request)
    {
        var contentType = request.ContentType;
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var separatorIndex = contentType.IndexOf(';');
        var mediaType = separatorIndex >= 0 ? contentType[..separatorIndex] : contentType;
        return string.Equals(
            mediaType.Trim(),
            SynchronizationProtocol.OctetStreamContentType,
            StringComparison.OrdinalIgnoreCase);
    }

    private static IResult CreateInvalidContentTypeResult() =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            detail: $"Content-Type must be {SynchronizationProtocol.OctetStreamContentType}.");

    private static string? GetOptionalHeader(HttpRequest request, string headerName)
    {
        var value = request.Headers[headerName].ToString();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static bool TryGetRequiredHeader(
        HttpRequest request,
        string headerName,
        [NotNullWhen(true)] out string? value,
        [NotNullWhen(false)] out IResult? error)
    {
        value = request.Headers[headerName].ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            error = Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"Missing or empty {headerName} header.");
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryReadRecordedSourceRequestMetadata(
        HttpRequest request,
        [NotNullWhen(true)] out RecordedSourceRequestMetadata? metadata,
        [NotNullWhen(false)] out IResult? error)
    {
        metadata = null;
        if (!TryGetRequiredHeader(request, SynchronizationProtocol.SourceKindHeader, out var sourceKindValue, out error))
        {
            return false;
        }

        RecordedSessionSourceKind sourceKind;
        try
        {
            sourceKind = RecordedSessionSourceKindExtensions.FromStorageValue(sourceKindValue);
        }
        catch (ArgumentOutOfRangeException)
        {
            error = Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"Unknown {SynchronizationProtocol.SourceKindHeader} header value.");
            return false;
        }

        if (!TryGetRequiredHeader(request, SynchronizationProtocol.SourceNameHeader, out var sourceNameValue, out error))
        {
            return false;
        }

        var sourceName = Uri.UnescapeDataString(sourceNameValue);
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            error = Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"Missing or empty {SynchronizationProtocol.SourceNameHeader} header.");
            return false;
        }

        if (!TryGetRequiredHeader(request, SynchronizationProtocol.SchemaVersionHeader, out var schemaVersionValue, out error))
        {
            return false;
        }

        if (!int.TryParse(schemaVersionValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var schemaVersion))
        {
            error = Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"Invalid {SynchronizationProtocol.SchemaVersionHeader} header value.");
            return false;
        }

        if (!TryGetRequiredHeader(request, SynchronizationProtocol.SourceHashHeader, out var sourceHash, out error))
        {
            return false;
        }

        metadata = new RecordedSourceRequestMetadata(sourceKind, sourceName, schemaVersion, sourceHash);
        return true;
    }

    // Content-Length is client-declared, so the preallocation is capped: a peer
    // declaring the full body limit while sending a few bytes must not force a
    // 256 MiB buffer per request. Genuine large bodies grow the stream as read.
    private const int MaxPreallocatedRequestBodyBytes = 1024 * 1024;

    private static async Task<byte[]> ReadRequestBodyAsync(HttpRequest request)
    {
        var capacity = request.ContentLength is > 0 and <= int.MaxValue
            ? (int)Math.Min(request.ContentLength.Value, MaxPreallocatedRequestBodyBytes)
            : 0;
        using var stream = capacity > 0 ? new MemoryStream(capacity) : new MemoryStream();
        await request.Body.CopyToAsync(stream);
        return stream.ToArray();
    }

    private string? certPassword;
    private Makaretu.Dns.ServiceDiscovery? serviceDiscovery;
    private ServiceProfile? advertisedService;
    private WebApplication? application;
    private Task<ServerStartResult>? startTask;

    private readonly string certPath;
    private readonly TimeProvider timeProvider;

    private Task Initialization { get; }

    public event EventHandler<SynchronizationActivityEventArgs>? SyncActivityStarted;
    public event EventHandler<SynchronizationActivityEventArgs>? SyncActivityEnded;

    #region Constructors

    public SynchronizationServerService(
        IInboundSyncCoordinator inboundSyncCoordinator,
        ISynchronizationTokenService tokens,
        ISessionRepository sessionRepository,
        IRecordedSessionSourceRepository recordedSessionSourceRepository,
        IRecordedSessionSourceSyncQuery recordedSessionSourceSyncQuery,
        ISecureStorage secureStorage,
        ISessionBlobSwapRequestStore swapRequestStore,
        IAppPaths paths,
        TimeProvider timeProvider)
    {
        this.inboundSyncCoordinator = inboundSyncCoordinator;
        this.tokens = tokens;
        this.sessionRepository = sessionRepository;
        this.recordedSessionSourceRepository = recordedSessionSourceRepository;
        this.recordedSessionSourceSyncQuery = recordedSessionSourceSyncQuery;
        this.secureStorage = secureStorage;
        this.swapRequestStore = swapRequestStore;
        certPath = paths.CertificatePath;
        this.timeProvider = timeProvider;
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

            var allAddresses = MulticastService.GetIPAddresses().ToList();
            var addresses = SelectAdvertisedAddresses(allAddresses).ToList();
            logger.Information(
                "Synchronization advertising candidate addresses count {Count} all {All} advertising {Advertising}",
                allAddresses.Count,
                string.Join(",", allAddresses),
                string.Join(",", addresses));
            if (addresses.Count == 0)
            {
                logger.Warning("Synchronization advertising has no routable addresses after filtering loopback and link-local addresses");
            }

            var discovery = new Makaretu.Dns.ServiceDiscovery();
            foreach (var instanceName in CreateServiceInstanceNames())
            {
                var service = new ServiceProfile(instanceName, SynchronizationProtocol.ServiceType, Port, addresses);
                if (discovery.Probe(service))
                {
                    logger.Warning(
                        "Synchronization service instance name {InstanceName} conflicted during mDNS probe; retrying with another name",
                        instanceName);
                    continue;
                }

                discovery.Advertise(service);
                discovery.Announce(service);
                advertisedService = service;
                serviceDiscovery = discovery;
                logger.Information(
                    "Synchronization service advertised as {InstanceName} on port {Port} with addresses {Addresses}",
                    instanceName,
                    Port,
                    string.Join(",", addresses));
                return;
            }

            discovery.Dispose();
            throw new InvalidOperationException("Could not advertise synchronization service because all mDNS service instance names conflicted.");
        }
    }

    public static IReadOnlyList<IPAddress> SelectAdvertisedAddresses(IEnumerable<IPAddress> addresses)
    {
        return addresses
            .Where(IsAdvertisableAddress)
            .OrderBy(address => address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
            .ToList();
    }

    private static bool IsAdvertisableAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6None))
        {
            return false;
        }

        if (address.IsIPv6LinkLocal)
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes is not [169, 254, _, _];
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6;
    }

    public static IEnumerable<string> CreateServiceInstanceNames()
    {
        yield return DefaultServiceInstanceName;

        for (var attempt = 2; attempt <= MaxServiceProbeAttempts; attempt++)
        {
            yield return $"{DefaultServiceInstanceName}-{attempt}";
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
        await tokens.GetSigningKeyAsync();

        await GenerateCertificateIfNeeded();
    }

    private async Task GenerateCertificate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(certPath)!);
        logger.Verbose("Generating synchronization server certificate at {CertificatePath}", certPath);

        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest(SynchronizationProtocol.CertificateSubjectName, ecdsa, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // Server Auth
            critical: false));
        var now = timeProvider.GetUtcNow();
        var cert = req.CreateSelfSigned(now, now.AddYears(5));
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
        if (cert.NotAfter < timeProvider.GetLocalNow())
        {
            logger.Verbose("Synchronization certificate expired at {CertificateExpiry}; generating a new certificate", cert.NotAfter);
            await GenerateCertificate();
        }
    }

    private async Task<WebApplication> BuildApplication(int port)
    {
        await Initialization;

        logger.Verbose("Building synchronization server pipeline for port {Port}", port);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: false);

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = MaxSyncRequestBodyBytes;
            options.ConfigureHttpsDefaults(httpsOptions =>
            {
                httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
            });
            options.Listen(IPAddress.IPv6Any, port, listenOptions =>
            {
                listenOptions.UseHttps(certPath, certPassword);
            });
        });

        // Keeps the client's AppJsonContext snake_case enum strings while hardening
        // inbound [FromBody] binding. These five fields mirror the .NET 10 Strict
        // preset exactly; they are set individually rather than assigning the preset
        // because this same options instance also serializes every Results.Ok(...)
        // response.
        builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(jsonOptions =>
        {
            var o = jsonOptions.SerializerOptions;
            o.AllowDuplicateProperties = false;                    // reject duplicate JSON keys
            o.RespectNullableAnnotations = true;                   // non-nullable members required
            o.RespectRequiredConstructorParameters = true;         // non-nullable positional-record ctor params required
            o.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            o.PropertyNameCaseInsensitive = false;                 // case-sensitive binding
            o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        });

        var key = await tokens.GetSigningKeyAsync();
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
        builder.Services.AddProblemDetails();

        builder.Services.AddRateLimiter(rateLimiterOptions =>
        {
            rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Fixed window keyed by real peer IP; window == the 6-digit PIN's 30 s TTL so
            // total guesses per source per PIN lifetime are bounded. No queue: excess
            // pairing attempts are rejected immediately, not pipelined.
            rateLimiterOptions.AddPolicy("pairing", httpContext =>
            {
                var remoteIp = httpContext.Connection.RemoteIpAddress;
                if (remoteIp is not null && remoteIp.IsIPv4MappedToIPv6)
                {
                    remoteIp = remoteIp.MapToIPv4();   // one client == one bucket on dual-stack
                }
                var partitionKey = remoteIp?.ToString() ?? "unknown";   // fail closed

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromSeconds(SynchronizationProtocol.PinTtlSeconds),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true,
                    });
            });

            rateLimiterOptions.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)TimeSpan.FromSeconds(SynchronizationProtocol.PinTtlSeconds).TotalSeconds)
                        .ToString(System.Globalization.CultureInfo.InvariantCulture);
                logger.Warning("Pairing request from {RemoteIp} rejected by rate limiter",
                    context.HttpContext.Connection.RemoteIpAddress);
                return ValueTask.CompletedTask;
            };
        });

        return builder.Build();
    }

    private IResult RunSyncActivity(
        SynchronizationProgressSnapshot progress,
        Func<Task<IResult>> action)
    {
        return new SynchronizationActivityResult(this, progress, action);
    }

    private static SynchronizationProgressSnapshot SyncActivity(
        SynchronizationPhase phase,
        string message) =>
        new(phase, message, CurrentStep: 0, TotalSteps: 0, IsDeterminate: false);

    internal static IResult MapPairingCommand(PairingCommandResult result) => result switch
    {
        PairingCommandResult.Applied => Results.Ok(),
        PairingCommandResult.Rejected => Results.Unauthorized(),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
    };

    internal static IResult MapPairingTokens(PairingTokenResult result) => result switch
    {
        PairingTokenResult.Issued issued => Results.Ok(issued.Tokens),
        PairingTokenResult.Rejected => Results.Unauthorized(),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
    };

    internal static async Task<IResult> ApplySynchronizationPushAsync(
        SynchronizationData data, IInboundSyncCoordinator inboundSyncCoordinator) =>
        await inboundSyncCoordinator.ApplyPushAsync(data) == InboundSyncWriteResult.Applied
            ? Results.NoContent() : Results.StatusCode(StatusCodes.Status500InternalServerError);

    internal static async Task<IResult> ApplySessionDataPatchAsync(
        Guid id, SessionBlobPayload payload, IInboundSyncCoordinator coordinator)
    {
        var result = await coordinator.ApplySessionBlobAsync(id, payload);
        if (result is SessionDataApplyResult.InvalidPayload or SessionDataApplyResult.Pending) return Results.Conflict();
        if (result == SessionDataApplyResult.Missing) return Results.NotFound();
        if (result == SessionDataApplyResult.Failed) return Results.StatusCode(StatusCodes.Status500InternalServerError);
        return Results.NoContent();
    }

    internal static async Task<IResult> ApplySourcePatchAsync(
        RecordedSessionSourcePayload payload, IInboundSyncCoordinator coordinator) =>
        await coordinator.ApplySourceAsync(payload) switch
        {
            InboundSourceApplyResult.Applied => Results.NoContent(),
            InboundSourceApplyResult.InvalidPayload => Results.Conflict(),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };

    private void RaiseSyncActivityStarted(SynchronizationProgressSnapshot progress)
    {
        SyncActivityStarted?.Invoke(this, new SynchronizationActivityEventArgs(progress));
    }

    private void RaiseSyncActivityEnded(SynchronizationProgressSnapshot progress)
    {
        SyncActivityEnded?.Invoke(this, new SynchronizationActivityEventArgs(progress));
    }

    private sealed class SynchronizationActivityResult(
        SynchronizationServerService owner,
        SynchronizationProgressSnapshot progress,
        Func<Task<IResult>> action) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            owner.RaiseSyncActivityStarted(progress);
            try
            {
                var result = await action();
                await result.ExecuteAsync(httpContext);
            }
            finally
            {
                owner.RaiseSyncActivityEnded(progress);
            }
        }
    }

    #endregion Private methods

    #region Public methods

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The synchronization server is a desktop-only feature and these minimal API delegates are explicitly rooted in this method.")]
    public Task<ServerStartResult> StartAsync()
    {
        lock (startGate)
        {
            if (application is not null)
            {
                return Task.FromResult<ServerStartResult>(new ServerStartResult.Started());
            }

            startTask ??= StartCoreAsync();
            return startTask;
        }
    }

    public async Task StopAsync()
    {
        WebApplication? app;
        lock (startGate)
        {
            app = application;
            application = null;
        }

        StopAdvertising();
        if (app is null) return;
        try { await app.StopAsync(); }
        finally { await app.DisposeAsync(); }
    }

    private async Task<ServerStartResult> StartCoreAsync()
    {
        WebApplication? app = null;

        try
        {
            logger.Information("Starting synchronization server on port {Port}", Port);

            app = await BuildApplication(Port);
            app.Lifetime.ApplicationStopping.Register(StopAdvertising);
            app.Lifetime.ApplicationStopped.Register(StopAdvertising);
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRateLimiter();
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

            // Anonymous, rate-limited pairing surface.
            var pairing = app.MapGroup("").RequireRateLimiting("pairing");
            pairing.AddEndpointFilter(RequireSyncProtocolVersionAsync);

            pairing.MapPost(SynchronizationProtocol.EndpointPairRequest, async ([FromBody] PairingRequest request) =>
                MapPairingCommand(await inboundSyncCoordinator.RequestPairingAsync(request)));
            pairing.MapPost(SynchronizationProtocol.EndpointPairConfirm, async ([FromBody] PairingConfirm request) =>
                MapPairingTokens(await inboundSyncCoordinator.ConfirmPairingAsync(request)));
            pairing.MapPost(SynchronizationProtocol.EndpointPairRefresh, async ([FromBody] RefreshRequest request) =>
                MapPairingTokens(await inboundSyncCoordinator.RefreshPairingAsync(request)));
            pairing.MapPost(SynchronizationProtocol.EndpointPairUnpair, async ([FromBody] UnpairRequest request) =>
                MapPairingCommand(await inboundSyncCoordinator.UnpairAsync(request)));

            // Authenticated sync surface — one RequireAuthorization for the whole group.
            var authorized = app.MapGroup("").RequireAuthorization();
            authorized.AddEndpointFilter(RequireSyncProtocolVersionAsync);

            authorized.MapGet(SynchronizationProtocol.EndpointSyncPull, ([FromQuery] long since, ClaimsPrincipal user) =>
            {
                return RunSyncActivity(
                    SyncActivity(SynchronizationPhase.ServingChanges, "Serving remote changes"),
                    async () =>
                    {
                        var data = await inboundSyncCoordinator.GetPullAsync(since);
                        var upperInclusive = data.UpperBound;

                        logger.Verbose(
                            "Synchronization pull in ({SinceExclusive}, {UpperInclusive}] returned {BoardCount} boards, {BikeCount} bikes, {SetupCount} setups, {SessionCount} sessions, {TrackCount} tracks",
                            since,
                            upperInclusive,
                            data.Boards.Count,
                            data.Bikes.Count,
                            data.Setups.Count,
                            data.Sessions.Count,
                            data.Tracks.Count);

                        return Results.Ok(data);
                    });
            });

            authorized.MapPut(SynchronizationProtocol.EndpointSyncPush, ([FromBody] SynchronizationData data, ClaimsPrincipal user) =>
            {
                return RunSyncActivity(
                    SyncActivity(SynchronizationPhase.ReceivingChanges, "Receiving remote changes"),
                    async () =>
                    {
                        logger.Verbose(
                            "Synchronization push received with {BoardCount} boards, {BikeCount} bikes, {SetupCount} setups, {SessionCount} sessions, {TrackCount} tracks",
                            data.Boards.Count,
                            data.Bikes.Count,
                            data.Setups.Count,
                            data.Sessions.Count,
                            data.Tracks.Count);

                        return await ApplySynchronizationPushAsync(data, inboundSyncCoordinator);
                    });
            });

            authorized.MapGet(SynchronizationProtocol.EndpointSessionIncomplete, (ClaimsPrincipal user) =>
            {
                return RunSyncActivity(
                    SyncActivity(SynchronizationPhase.CheckingIncompleteSessions, "Checking missing session data"),
                    async () =>
                    {
                        var incompleteSessions = await sessionRepository.GetIncompleteSessionIdsAsync();
                        var pendingSwaps = await swapRequestStore.GetRequestedSessionIdsAsync();
                        // Advertise both rows with no BLOB (fills) and held-BLOB rows the
                        // hub wants a newer BLOB for (push-swaps), so a client uploads the
                        // matching bytes. The two sets are disjoint, but Union guards overlap.
                        var requestedSessions = incompleteSessions.Union(pendingSwaps).ToList();
                        logger.Verbose(
                            "Synchronization incomplete-session query returned {SessionCount} sessions ({FillCount} fills, {SwapCount} push-swaps)",
                            requestedSessions.Count,
                            incompleteSessions.Count,
                            pendingSwaps.Count);
                        return Results.Ok(requestedSessions);
                    });
            });

            authorized.MapGet($"{SynchronizationProtocol.EndpointSessionData}{{id:guid}}", ([FromRoute] Guid id, HttpResponse response, ClaimsPrincipal user) =>
            {
                return RunSyncActivity(
                    SyncActivity(SynchronizationPhase.ServingSessionData, "Serving session data"),
                    async () =>
                    {
                        var blob = await sessionRepository.GetSessionRawPsstWithFingerprintAsync(id);
                        if (blob is null)
                        {
                            logger.Warning("Session data download failed because session {SessionId} was not found", id);
                            return Results.Problem(statusCode: StatusCodes.Status404NotFound, detail: "Session does not exist.");
                        }

                        logger.Verbose("Serving session data for {SessionId} with {ByteCount} bytes", id, blob.Value.Data.Length);

                        if (!string.IsNullOrEmpty(blob.Value.Fingerprint))
                        {
                            response.Headers[SynchronizationProtocol.FingerprintHeader] = blob.Value.Fingerprint;
                        }

                        return Results.Bytes(blob.Value.Data, SynchronizationProtocol.OctetStreamContentType);
                    });
            });

            authorized.MapPatch($"{SynchronizationProtocol.EndpointSessionData}{{id:guid}}", ([FromRoute] Guid id, HttpRequest request, ClaimsPrincipal user) =>
            {
                return RunSyncActivity(
                    SyncActivity(SynchronizationPhase.ReceivingSessionData, "Receiving session data"),
                    async () =>
                    {
                        if (!IsOctetStreamRequest(request))
                        {
                            logger.Warning("Session data patch rejected because request content type was invalid for {SessionId}", id);
                            return CreateInvalidContentTypeResult();
                        }

                        var payload = new SessionBlobPayload(
                            GetOptionalHeader(request, SynchronizationProtocol.FingerprintHeader),
                            await ReadRequestBodyAsync(request));

                        return await ApplySessionDataPatchAsync(
                            id,
                            payload,
                            inboundSyncCoordinator);
                    });
            });

            authorized.MapGet(SynchronizationProtocol.EndpointSessionSourceIncomplete, (ClaimsPrincipal user) =>
            {
                return RunSyncActivity(
                    SyncActivity(SynchronizationPhase.CheckingIncompleteSessionSources, "Checking missing recorded sources"),
                    async () =>
                    {
                        var incompleteSources = await recordedSessionSourceSyncQuery.GetSourceSyncTargetIdsAsync();
                        logger.Verbose("Synchronization incomplete-session-source query returned {SourceCount} sessions", incompleteSources.Count);
                        return Results.Ok(incompleteSources);
                    });
            });

            authorized.MapGet($"{SynchronizationProtocol.EndpointSessionSourceData}{{id:guid}}", ([FromRoute] Guid id, HttpResponse response, ClaimsPrincipal user) =>
            {
                return RunSyncActivity(
                    SyncActivity(SynchronizationPhase.ServingSessionSourceData, "Serving recorded source data"),
                    async () =>
                    {
                        var source = await recordedSessionSourceRepository.GetRecordedSessionSourceAsync(id);
                        if (source is null)
                        {
                            logger.Warning("Recorded source download failed because source {SessionId} was not found", id);
                            return Results.Problem(statusCode: StatusCodes.Status404NotFound, detail: "Recorded source does not exist.");
                        }

                        logger.Verbose("Serving recorded source for {SessionId} with {ByteCount} bytes", id, source.Payload.Length);
                        response.Headers[SynchronizationProtocol.SourceKindHeader] = source.SourceKind.StorageValue;
                        response.Headers[SynchronizationProtocol.SourceNameHeader] = Uri.EscapeDataString(source.SourceName);
                        response.Headers[SynchronizationProtocol.SchemaVersionHeader] = source.SchemaVersion.ToString(CultureInfo.InvariantCulture);
                        response.Headers[SynchronizationProtocol.SourceHashHeader] = source.SourceHash;
                        return Results.Bytes(source.Payload, SynchronizationProtocol.OctetStreamContentType);
                    });
            });

            authorized.MapPatch($"{SynchronizationProtocol.EndpointSessionSourceData}{{id:guid}}", ([FromRoute] Guid id, HttpRequest request, ClaimsPrincipal user) =>
            {
                return RunSyncActivity(
                    SyncActivity(SynchronizationPhase.ReceivingSessionSourceData, "Receiving recorded source data"),
                    async () =>
                    {
                        if (!IsOctetStreamRequest(request))
                        {
                            logger.Warning("Recorded source patch rejected because request content type was invalid for {SessionId}", id);
                            return CreateInvalidContentTypeResult();
                        }

                        if (!TryReadRecordedSourceRequestMetadata(request, out var metadata, out var metadataError))
                        {
                            logger.Warning("Recorded source patch rejected because metadata headers were invalid for {SessionId}", id);
                            return metadataError;
                        }

                        var transfer = new RecordedSessionSourcePayload(
                            id,
                            metadata.SourceKind,
                            metadata.SourceName,
                            metadata.SchemaVersion,
                            metadata.SourceHash,
                            await ReadRequestBodyAsync(request));

                        return await ApplySourcePatchAsync(transfer, inboundSyncCoordinator);
                    });
            });

            await app.StartAsync();

            lock (startGate)
            {
                application = app;
                startTask = null;
            }

            logger.Information("Synchronization server listening on port {Port}", Port);
            logger.Verbose("Advertising synchronization service on port {Port}", Port);
            StartAdvertising();
            return new ServerStartResult.Started();
        }
        catch (Exception ex)
        {
            lock (startGate)
            {
                if (ReferenceEquals(application, app))
                {
                    application = null;
                }

                startTask = null;
            }

            StopAdvertising();
            if (app is not null)
            {
                await StopAndDisposeAfterStartFailureAsync(app);
            }

            logger.Error(ex, "Synchronization server failed to start on port {Port}", Port);
            return new ServerStartResult.Failed(ServerStartFailureReason.Start, ex.Message);
        }
    }

    private async Task StopAndDisposeAfterStartFailureAsync(WebApplication app)
    {
        try
        {
            await app.StopAsync();
        }
        catch (Exception stopException)
        {
            logger.Warning(stopException, "Stopping partially started synchronization server failed on port {Port}", Port);
        }

        try
        {
            await app.DisposeAsync();
        }
        catch (Exception disposeException)
        {
            logger.Warning(disposeException, "Disposing partially started synchronization server failed on port {Port}", Port);
        }
    }

    #endregion
}
