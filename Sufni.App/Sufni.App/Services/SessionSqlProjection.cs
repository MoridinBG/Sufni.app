namespace Sufni.App.Services;

/// <summary>
/// Session SQL fragments shared by the session repository and the
/// synchronization merge engine.
/// </summary>
internal static class SessionSqlProjection
{
    public const string ProcessingFingerprintColumn = "session_processing_fingerprint";

    public const string HasDataProjection = """
                                            CASE
                                               WHEN data IS NOT NULL THEN 1
                                               ELSE 0
                                            END AS has_data
                                            """;
}
