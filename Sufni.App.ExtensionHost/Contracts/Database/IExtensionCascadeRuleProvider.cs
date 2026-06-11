using System.Collections.Generic;

namespace Sufni.App.ExtensionHost.Contracts.Database;

public interface IExtensionCascadeRuleProvider
{
    IReadOnlyList<ExtensionCascadeRule> Rules { get; }
}

