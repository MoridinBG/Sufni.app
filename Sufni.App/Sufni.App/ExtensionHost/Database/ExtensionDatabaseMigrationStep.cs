using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.ExtensionHost.Database;

public sealed record ExtensionDatabaseMigrationStep(
    int TargetVersion,
    Func<ExtensionDatabaseMigrationContext, CancellationToken, Task> ApplyAsync);

