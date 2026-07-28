using System;

namespace Sufni.App.ExtensionHost.Contracts.Database;

public sealed record ExtensionDatabaseMigrationStep(
    int TargetVersion,
    Action<ExtensionDatabaseMigrationContext> Apply);

