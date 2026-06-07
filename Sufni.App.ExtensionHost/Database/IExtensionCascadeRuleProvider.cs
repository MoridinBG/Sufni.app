using System.Collections.Generic;

namespace Sufni.App.ExtensionHost.Database;

public interface IExtensionCascadeRuleProvider
{
    IReadOnlyList<ExtensionCascadeRule> Rules { get; }
}

