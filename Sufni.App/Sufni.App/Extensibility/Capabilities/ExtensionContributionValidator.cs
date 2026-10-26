using System;
using System.Collections.Generic;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

namespace Sufni.App.Extensibility.Capabilities;

internal static class ExtensionContributionValidator
{
    public static void ValidateRecordedSessionExtensionFactories(
        IReadOnlyList<IRecordedSessionExtensionFactory> factories)
    {
        var extensionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var factory in factories)
        {
            ArgumentNullException.ThrowIfNull(factory);
            ValidateRequiredId(factory.ExtensionId, "Recorded-session extension factory");
            if (!extensionIds.Add(factory.ExtensionId))
            {
                throw new InvalidOperationException(
                    $"More than one recorded-session extension factory is registered for extension '{factory.ExtensionId}'.");
            }
        }
    }

    private static void ValidateRequiredId(string id, string owner)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException($"{owner} id is required.");
        }
    }
}
