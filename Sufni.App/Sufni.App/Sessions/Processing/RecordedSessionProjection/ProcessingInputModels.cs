using System;

using Sufni.App.Bikes.Models;
using Sufni.App.Sessions.Store;
namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

public sealed record SessionProcessingInput(
    Guid Id,
    Guid SetupId);

public sealed record SetupProcessingInput(
    Guid Id,
    Guid BikeId,
    string? FrontSensorConfigurationJson,
    string? RearSensorConfigurationJson);

public sealed record BikeProcessingInput(
    Guid Id,
    double HeadAngle,
    double? ForkStroke,
    double? ShockStroke,
    RearSuspensionSpec RearSuspension)
{
    public RearSuspensionKind Kind => RearSuspension.Kind;
}

public sealed record SessionProcessingInputBundle(
    SessionProcessingInput Session,
    SetupProcessingInput Setup,
    BikeProcessingInput Bike,
    RecordedSessionSourceSnapshot Source);
