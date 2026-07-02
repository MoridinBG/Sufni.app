using System;
using System.Text.Json.Serialization;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.Models;

[JsonConverter(typeof(RearSuspensionSpecJsonConverter))]
public abstract record RearSuspensionSpec
{
    private RearSuspensionSpec() { }

    public sealed record Hardtail : RearSuspensionSpec;

    public sealed record LinkageDraft : RearSuspensionSpec;

    public sealed record LeverageRatioDraft : RearSuspensionSpec;

    public sealed record Linkage(LinkageSpec Spec) : RearSuspensionSpec;

    public sealed record LeverageRatio(LeverageRatioSpec Spec) : RearSuspensionSpec;

    [JsonIgnore]
    public RearSuspensionKind Kind => this switch
    {
        Hardtail => RearSuspensionKind.None,
        LinkageDraft => RearSuspensionKind.Linkage,
        Linkage => RearSuspensionKind.Linkage,
        LeverageRatioDraft => RearSuspensionKind.LeverageRatio,
        LeverageRatio => RearSuspensionKind.LeverageRatio,
        _ => throw new ArgumentOutOfRangeException(nameof(RearSuspensionSpec), GetType().Name)
    };
}
