using System;
using System.Collections.Generic;

namespace Sufni.App.ExtensionHost.Database;

public interface IExtensionDatabaseMigrator
{
    string ExtensionId { get; }
    int TargetVersion { get; }
    IReadOnlyList<Type> TableTypes { get; }
    IReadOnlyList<ExtensionDatabaseMigrationStep> Steps { get; }
}

