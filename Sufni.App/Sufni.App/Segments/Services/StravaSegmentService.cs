using Serilog;
using Sufni.App.Shared.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;
using Sufni.Telemetry;

using Sufni.App.Segments.Models;
namespace Sufni.App.Segments.Services;

/// <summary>
/// Reads Strava segment efforts with the user's own Strava API app. The
/// client credentials and OAuth tokens live in secure storage. Fetch results
/// are cached per session timing until the connection changes.
/// </summary>
public interface IStravaSegmentService
{
    Task<StravaConnectionSnapshot> GetConnectionSnapshotAsync(CancellationToken cancellationToken = default);

    string CreateAuthorizationUrl(string clientId);

    /// <summary>
    /// Exchanges the code in the redirect URL the user pasted for tokens and
    /// stores the credentials.
    /// </summary>
    Task<StravaConnectResult> ConnectAsync(
        StravaConnectRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the activity that overlaps the session most and returns its
    /// segment efforts that fall inside the session.
    /// </summary>
    Task<StravaSegmentFetchResult> FindSegmentsAsync(
        DateTimeOffset sessionStart,
        double sessionDurationSeconds,
        CancellationToken cancellationToken = default);

    Task ResetCredentialsAsync(CancellationToken cancellationToken = default);
}

internal sealed class StravaSegmentService : IStravaSegmentService
{
    private static readonly ILogger logger = Log.ForContext<StravaSegmentService>();
    private const string AuthorizationEndpoint = "https://www.strava.com/oauth/authorize";
    private const string TokenEndpoint = "https://www.strava.com/oauth/token";
    private const string ActivitiesEndpoint = "https://www.strava.com/api/v3/athlete/activities";
    private const string ActivityEndpoint = "https://www.strava.com/api/v3/activities";
    private const string RequiredScope = "activity:read_all";
    private const string RequestedScope = "read,activity:read_all";
    private const string RedirectUri = "http://localhost/exchange_token";
    private static readonly TimeSpan TokenRefreshMargin = TimeSpan.FromSeconds(30);

    private readonly ISecureStorage secureStorage;
    private readonly HttpClient httpClient;
    private readonly TimeProvider timeProvider;
    private readonly System.Threading.Lock segmentCacheLock = new();
    private readonly Dictionary<SegmentCacheKey, StravaSegmentFetchResult> segmentCache = [];

    public StravaSegmentService(ISecureStorage secureStorage, TimeProvider timeProvider)
        : this(secureStorage, new HttpClient(), timeProvider)
    {
    }

    public StravaSegmentService(
        ISecureStorage secureStorage,
        HttpClient httpClient,
        TimeProvider timeProvider)
    {
        this.secureStorage = secureStorage;
        this.httpClient = httpClient;
        this.timeProvider = timeProvider;
    }

    public async Task<StravaConnectionSnapshot> GetConnectionSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var clientId = await secureStorage.GetStringAsync(StravaSecureStorageKeys.ClientId);
        var clientSecret = await secureStorage.GetStringAsync(StravaSecureStorageKeys.ClientSecret);
        var refreshToken = await secureStorage.GetStringAsync(StravaSecureStorageKeys.RefreshToken);
        var scope = await secureStorage.GetStringAsync(StravaSecureStorageKeys.Scope);

        return new StravaConnectionSnapshot(
            clientId,
            clientSecret,
            IsConnectionUsable(clientId, clientSecret, refreshToken, scope));
    }

    public string CreateAuthorizationUrl(string clientId)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = RedirectUri,
            ["response_type"] = "code",
            ["approval_prompt"] = "force",
            ["scope"] = RequestedScope,
        };

        return $"{AuthorizationEndpoint}?{BuildQuery(query)}";
    }

    public async Task<StravaConnectResult> ConnectAsync(
        StravaConnectRequest request,
        CancellationToken cancellationToken = default)
    {
        var clientId = NormalizeRequired(request.ClientId);
        var clientSecret = NormalizeRequired(request.ClientSecret);
        var redirectUrl = NormalizeRequired(request.RedirectUrl);

        if (clientId is null || clientSecret is null)
        {
            return new StravaConnectResult.ValidationFailed("Client ID and client secret are required.");
        }

        if (redirectUrl is null)
        {
            return new StravaConnectResult.ValidationFailed("Redirect URL is required.");
        }

        var authorizationCode = ExtractAuthorizationCode(redirectUrl);
        if (authorizationCode is null)
        {
            return new StravaConnectResult.ValidationFailed("Redirect URL does not contain a code.");
        }

        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = authorizationCode,
            ["grant_type"] = "authorization_code",
        };

        using var response = await httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new StravaConnectResult.RateLimited();
        }

        if (!response.IsSuccessStatusCode)
        {
            return new StravaConnectResult.Failed(StravaConnectFailureReason.Connect, DescribeResponseFailure(response, TokenEndpoint));
        }

        var tokenResponse = await ReadJsonAsync<StravaTokenResponseDto>(response.Content, cancellationToken);
        if (!IsTokenResponseUsable(tokenResponse))
        {
            logger.Error("Strava token response was incomplete during authorization");
            return new StravaConnectResult.Failed(StravaConnectFailureReason.Connect, "Strava token response was incomplete.");
        }

        var validTokenResponse = tokenResponse!;
        if (!ScopeIncludesRequiredScope(validTokenResponse.Scope))
        {
            await secureStorage.SetStringAsync(StravaSecureStorageKeys.ClientId, clientId);
            await secureStorage.SetStringAsync(StravaSecureStorageKeys.ClientSecret, clientSecret);
            await ClearOAuthStateAsync();
            return new StravaConnectResult.MissingRequiredScope();
        }

        await PersistCredentialsAsync(clientId, clientSecret, validTokenResponse);
        return new StravaConnectResult.Connected();
    }

    public async Task<StravaSegmentFetchResult> FindSegmentsAsync(
        DateTimeOffset sessionStart,
        double sessionDurationSeconds,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(sessionDurationSeconds) || sessionDurationSeconds <= 0)
        {
            return new StravaSegmentFetchResult.MissingSessionTiming();
        }

        var cacheKey = SegmentCacheKey.Create(sessionStart, sessionDurationSeconds);
        if (TryGetCachedSegmentResult(cacheKey, out var cachedResult))
        {
            return cachedResult;
        }

        var tokenResult = await GetFreshAccessTokenAsync(cancellationToken);
        switch (tokenResult)
        {
            case AccessTokenResult.NotConnected:
                return new StravaSegmentFetchResult.NotConnected();
            case AccessTokenResult.ReconnectRequired:
                return new StravaSegmentFetchResult.ReconnectRequired();
            case AccessTokenResult.RateLimited:
                return new StravaSegmentFetchResult.RateLimited();
            case AccessTokenResult.Failed failed:
                return new StravaSegmentFetchResult.Failed(StravaSegmentFetchFailureReason.Load, failed.Details);
            case AccessTokenResult.Available:
                break;
        }

        var token = ((AccessTokenResult.Available)tokenResult).AccessToken;
        var sessionEnd = sessionStart.AddSeconds(sessionDurationSeconds);
        var after = sessionStart.AddSeconds(-43_200).ToUnixTimeSeconds();
        var before = sessionEnd.AddSeconds(7_200).ToUnixTimeSeconds();
        var activitiesUri = $"{ActivitiesEndpoint}?{BuildQuery(new Dictionary<string, string?> {
            ["after"] = after.ToString(CultureInfo.InvariantCulture),
            ["before"] = before.ToString(CultureInfo.InvariantCulture),
            ["per_page"] = "50",
        })}";

        var activitiesResult = await GetJsonAsync<List<StravaActivitySummaryDto>>(activitiesUri, token, cancellationToken);
        switch (activitiesResult)
        {
            case JsonResult.RateLimited:
                return new StravaSegmentFetchResult.RateLimited();
            case JsonResult.Unauthorized:
                await ClearOAuthStateAsync();
                return new StravaSegmentFetchResult.ReconnectRequired();
            case JsonResult.Failed failed:
                return new StravaSegmentFetchResult.Failed(StravaSegmentFetchFailureReason.Load, failed.Details);
            case JsonResult.Success<List<StravaActivitySummaryDto>>:
                break;
        }

        var activities = ((JsonResult.Success<List<StravaActivitySummaryDto>>)activitiesResult).Value ?? [];
        var activity = FindBestActivity(activities, sessionStart, sessionEnd);
        if (activity is null)
        {
            return CacheSegmentResult(cacheKey, new StravaSegmentFetchResult.NoMatchingActivity());
        }

        var detailUri = $"{ActivityEndpoint}/{activity.Activity.Id.ToString(CultureInfo.InvariantCulture)}?include_all_efforts=true";
        var detailResult = await GetJsonAsync<StravaDetailedActivityDto>(detailUri, token, cancellationToken);
        switch (detailResult)
        {
            case JsonResult.RateLimited:
                return new StravaSegmentFetchResult.RateLimited();
            case JsonResult.Unauthorized:
                await ClearOAuthStateAsync();
                return new StravaSegmentFetchResult.ReconnectRequired();
            case JsonResult.Failed failed:
                return new StravaSegmentFetchResult.Failed(StravaSegmentFetchFailureReason.Load, failed.Details);
            case JsonResult.Success<StravaDetailedActivityDto>:
                break;
        }

        var detailedActivity = ((JsonResult.Success<StravaDetailedActivityDto>)detailResult).Value;
        var segments = BuildSegmentOptions(
                (detailedActivity?.SegmentEfforts ?? []).OrderBy(effort => effort.StartDate),
                sessionStart,
                sessionDurationSeconds)
            .ToList();

        return CacheSegmentResult(
            cacheKey,
            segments.Count == 0
                ? new StravaSegmentFetchResult.NoUsableSegments()
                : new StravaSegmentFetchResult.Loaded(segments));
    }

    public async Task ResetCredentialsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClearSegmentCache();

        foreach (var key in StravaSecureStorageKeys.All)
        {
            await secureStorage.RemoveAsync(key);
        }
    }

    private async Task PersistCredentialsAsync(
        string clientId,
        string clientSecret,
        StravaTokenResponseDto tokenResponse)
    {
        ClearSegmentCache();
        await secureStorage.SetStringAsync(StravaSecureStorageKeys.ClientId, clientId);
        await secureStorage.SetStringAsync(StravaSecureStorageKeys.ClientSecret, clientSecret);
        await PersistTokensAsync(tokenResponse);
    }

    private async Task PersistTokensAsync(StravaTokenResponseDto tokenResponse)
    {
        await secureStorage.SetStringAsync(StravaSecureStorageKeys.AccessToken, tokenResponse.AccessToken);
        await secureStorage.SetStringAsync(StravaSecureStorageKeys.RefreshToken, tokenResponse.RefreshToken);
        await secureStorage.SetStringAsync(
            StravaSecureStorageKeys.AccessTokenExpiresAt,
            tokenResponse.ExpiresAt.ToString(CultureInfo.InvariantCulture));
        await secureStorage.SetStringAsync(StravaSecureStorageKeys.Scope, tokenResponse.Scope);
    }

    private async Task ClearOAuthStateAsync()
    {
        ClearSegmentCache();
        await secureStorage.RemoveAsync(StravaSecureStorageKeys.AccessToken);
        await secureStorage.RemoveAsync(StravaSecureStorageKeys.RefreshToken);
        await secureStorage.RemoveAsync(StravaSecureStorageKeys.AccessTokenExpiresAt);
        await secureStorage.RemoveAsync(StravaSecureStorageKeys.Scope);
    }

    private async Task<AccessTokenResult> GetFreshAccessTokenAsync(CancellationToken cancellationToken)
    {
        var clientId = await secureStorage.GetStringAsync(StravaSecureStorageKeys.ClientId);
        var clientSecret = await secureStorage.GetStringAsync(StravaSecureStorageKeys.ClientSecret);
        var accessToken = await secureStorage.GetStringAsync(StravaSecureStorageKeys.AccessToken);
        var refreshToken = await secureStorage.GetStringAsync(StravaSecureStorageKeys.RefreshToken);
        var expiresAt = await secureStorage.GetStringAsync(StravaSecureStorageKeys.AccessTokenExpiresAt);
        var scope = await secureStorage.GetStringAsync(StravaSecureStorageKeys.Scope);

        if (!IsConnectionUsable(clientId, clientSecret, refreshToken, scope))
        {
            return new AccessTokenResult.NotConnected();
        }

        if (!string.IsNullOrWhiteSpace(accessToken) &&
            TryParseUnixExpiry(expiresAt, out var expiry) &&
            expiry > timeProvider.GetUtcNow().Add(TokenRefreshMargin))
        {
            return new AccessTokenResult.Available(accessToken);
        }

        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId!,
            ["client_secret"] = clientSecret!,
            ["refresh_token"] = refreshToken!,
            ["grant_type"] = "refresh_token",
        };

        using var response = await httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await ClearOAuthStateAsync();
            return new AccessTokenResult.ReconnectRequired();
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new AccessTokenResult.RateLimited();
        }

        if (!response.IsSuccessStatusCode)
        {
            return new AccessTokenResult.Failed(DescribeResponseFailure(response, TokenEndpoint));
        }

        var tokenResponse = await ReadJsonAsync<StravaTokenResponseDto>(response.Content, cancellationToken);
        if (!IsTokenResponseUsable(tokenResponse))
        {
            logger.Error("Strava token response was incomplete during refresh");
            return new AccessTokenResult.Failed("Strava token response was incomplete.");
        }

        var validTokenResponse = tokenResponse!;
        var returnedScope = string.IsNullOrWhiteSpace(validTokenResponse.Scope) ? scope : validTokenResponse.Scope;
        if (!ScopeIncludesRequiredScope(returnedScope))
        {
            await ClearOAuthStateAsync();
            return new AccessTokenResult.ReconnectRequired();
        }

        await PersistTokensAsync(validTokenResponse with { Scope = returnedScope });
        return new AccessTokenResult.Available(validTokenResponse.AccessToken!);
    }

    private async Task<JsonResult> GetJsonAsync<T>(
        string uri,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new JsonResult.RateLimited();
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new JsonResult.Unauthorized();
        }

        if (!response.IsSuccessStatusCode)
        {
            return new JsonResult.Failed(DescribeResponseFailure(response, uri));
        }

        var value = await ReadJsonAsync<T>(response.Content, cancellationToken);
        return new JsonResult.Success<T>(value);
    }

    private static string DescribeResponseFailure(HttpResponseMessage response, string endpoint)
    {
        var details = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
        if ((int)response.StatusCode < 500)
            logger.Warning("Strava request rejected by {Endpoint}: {Details}", endpoint, details);
        else
            logger.Error("Strava request failed at {Endpoint}: {Details}", endpoint, details);
        return details;
    }

    private bool TryGetCachedSegmentResult(
        SegmentCacheKey key,
        out StravaSegmentFetchResult result)
    {
        lock (segmentCacheLock)
        {
            return segmentCache.TryGetValue(key, out result!);
        }
    }

    private StravaSegmentFetchResult CacheSegmentResult(
        SegmentCacheKey key,
        StravaSegmentFetchResult result)
    {
        lock (segmentCacheLock)
        {
            segmentCache[key] = result;
        }

        return result;
    }

    private void ClearSegmentCache()
    {
        lock (segmentCacheLock)
        {
            segmentCache.Clear();
        }
    }

    private static IEnumerable<StravaSegmentOption> BuildSegmentOptions(
        IEnumerable<StravaSegmentEffortDto> efforts,
        DateTimeOffset sessionStart,
        double sessionDurationSeconds)
    {
        foreach (var effort in efforts)
        {
            var effortEnd = effort.StartDate.AddSeconds(effort.ElapsedTimeSeconds);
            if (!TelemetryTimeRange.TryCreateClamped(
                    (effort.StartDate - sessionStart).TotalSeconds,
                    (effortEnd - sessionStart).TotalSeconds,
                    sessionDurationSeconds,
                    out var range))
            {
                continue;
            }

            var segment = effort.Segment;
            var segmentName = !string.IsNullOrWhiteSpace(effort.Name)
                ? effort.Name!
                : segment?.Name ?? string.Empty;
            var distance = double.IsFinite(effort.DistanceMeters) && effort.DistanceMeters > 0
                ? effort.DistanceMeters
                : segment?.DistanceMeters ?? 0;

            yield return new StravaSegmentOption(segmentName, distance, range);
        }
    }

    private static ActivityOverlap? FindBestActivity(
        IEnumerable<StravaActivitySummaryDto> activities,
        DateTimeOffset sessionStart,
        DateTimeOffset sessionEnd)
    {
        return activities
            .Select(activity => new ActivityOverlap(
                activity,
                CalculateOverlapSeconds(
                    activity.StartDate,
                    activity.StartDate.AddSeconds(activity.ElapsedTimeSeconds),
                    sessionStart,
                    sessionEnd),
                Math.Abs((activity.StartDate - sessionStart).TotalSeconds)))
            .Where(overlap => overlap.OverlapSeconds > 0)
            .OrderByDescending(overlap => overlap.OverlapSeconds)
            .ThenBy(overlap => overlap.AbsoluteStartGapSeconds)
            .ThenBy(overlap => overlap.Activity.Id)
            .FirstOrDefault();
    }

    private static double CalculateOverlapSeconds(
        DateTimeOffset firstStart,
        DateTimeOffset firstEnd,
        DateTimeOffset secondStart,
        DateTimeOffset secondEnd)
    {
        var start = firstStart > secondStart ? firstStart : secondStart;
        var end = firstEnd < secondEnd ? firstEnd : secondEnd;
        return Math.Max(0, (end - start).TotalSeconds);
    }

    private static bool IsConnectionUsable(
        string? clientId,
        string? clientSecret,
        string? refreshToken,
        string? scope) =>
        !string.IsNullOrWhiteSpace(clientId) &&
        !string.IsNullOrWhiteSpace(clientSecret) &&
        !string.IsNullOrWhiteSpace(refreshToken) &&
        ScopeIncludesRequiredScope(scope);

    private static bool ScopeIncludesRequiredScope(string? scope) =>
        (scope ?? string.Empty)
        .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Contains(RequiredScope, StringComparer.Ordinal);

    private static bool IsTokenResponseUsable(StravaTokenResponseDto? tokenResponse) =>
        !string.IsNullOrWhiteSpace(tokenResponse?.AccessToken) &&
        !string.IsNullOrWhiteSpace(tokenResponse.RefreshToken) &&
        tokenResponse.ExpiresAt > 0;

    private static bool TryParseUnixExpiry(string? value, out DateTimeOffset expiry)
    {
        expiry = default;
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) &&
               seconds > 0 &&
               TryFromUnixTimeSeconds(seconds, out expiry);
    }

    private static bool TryFromUnixTimeSeconds(long seconds, out DateTimeOffset value)
    {
        try
        {
            value = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            value = default;
            return false;
        }
    }

    private static string? NormalizeRequired(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ExtractAuthorizationCode(string redirectUrl)
    {
        if (!Uri.TryCreate(redirectUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return NormalizeRequired(GetParameter(uri.Query, "code")) ??
               NormalizeRequired(GetParameter(uri.Fragment, "code"));
    }

    private static string? GetParameter(string source, string key)
    {
        foreach (var part in source.TrimStart('?', '#').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            if (!string.Equals(Uri.UnescapeDataString(pieces[0]), key, StringComparison.Ordinal))
            {
                continue;
            }

            return pieces.Length > 1
                ? Uri.UnescapeDataString(pieces[1].Replace("+", " ", StringComparison.Ordinal))
                : string.Empty;
        }

        return null;
    }

    private static string BuildQuery(IReadOnlyDictionary<string, string?> values)
    {
        return string.Join(
            "&",
            values.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value ?? string.Empty)}"));
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var typeInfo = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)
            StravaJsonContext.Default.GetTypeInfo(typeof(T))!;
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken);
    }

    private sealed record ActivityOverlap(
        StravaActivitySummaryDto Activity,
        double OverlapSeconds,
        double AbsoluteStartGapSeconds);

    private sealed record SegmentCacheKey(long SessionStartUnixMilliseconds, long SessionDurationMilliseconds)
    {
        public static SegmentCacheKey Create(DateTimeOffset sessionStart, double sessionDurationSeconds) =>
            new(
                sessionStart.ToUnixTimeMilliseconds(),
                (long)Math.Round(sessionDurationSeconds * 1000, MidpointRounding.AwayFromZero));
    }

    private abstract record AccessTokenResult
    {
        private AccessTokenResult() { }

        public sealed record Available(string AccessToken) : AccessTokenResult;
        public sealed record NotConnected : AccessTokenResult;
        public sealed record ReconnectRequired : AccessTokenResult;
        public sealed record RateLimited : AccessTokenResult;
        public sealed record Failed(string Details) : AccessTokenResult;
    }

    private abstract record JsonResult
    {
        private JsonResult() { }

        public sealed record Success<T>(T? Value) : JsonResult;
        public sealed record Unauthorized : JsonResult;
        public sealed record RateLimited : JsonResult;
        public sealed record Failed(string Details) : JsonResult;
    }
}
