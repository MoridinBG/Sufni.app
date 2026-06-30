using System.Collections.Generic;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Services;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

/// <summary>
/// The extension-host services a recorded-session editor needs when
/// recorded-session extension factories are registered. Grouped so the
/// all-or-nothing requirement is carried by the type instead of a ctor
/// null-combination check.
/// </summary>
internal sealed record ExtensionHostDependencies(
    IReadOnlyList<IRecordedSessionExtensionFactory> RecordedSessionExtensionFactories,
    IExtensionDatabaseConnection ExtensionDatabase,
    IRecordedSessionDataReader RecordedSessionDataReader,
    IBackgroundTaskRunner BackgroundTaskRunner);
