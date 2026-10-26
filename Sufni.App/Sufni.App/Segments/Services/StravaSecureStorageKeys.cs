namespace Sufni.App.Segments.Services;

// The names predate the fold; changing them would drop existing Strava connections.
internal static class StravaSecureStorageKeys
{
    public const string ClientId = "SegmentAnalysis.Strava.ClientId";
    public const string ClientSecret = "SegmentAnalysis.Strava.ClientSecret";
    public const string AccessToken = "SegmentAnalysis.Strava.AccessToken";
    public const string RefreshToken = "SegmentAnalysis.Strava.RefreshToken";
    public const string AccessTokenExpiresAt = "SegmentAnalysis.Strava.AccessTokenExpiresAt";
    public const string Scope = "SegmentAnalysis.Strava.Scope";

    public static readonly string[] All =
    [
        ClientId,
        ClientSecret,
        AccessToken,
        RefreshToken,
        AccessTokenExpiresAt,
        Scope,
    ];
}
