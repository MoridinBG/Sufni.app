using System;

namespace Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

/// <summary>
/// Source-absolute telemetry window used to derive a recorded session's
/// processed data from a recording source. A null end means the recording source
/// end.
/// </summary>
public sealed record RecordedSessionDerivationWindow(
    Guid SourceSessionId,
    double StartSeconds,
    double? EndSeconds);
