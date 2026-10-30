using System;

using Sufni.App.Sessions.Store;
namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

public sealed record SessionProcessingInput(
    Guid Id,
    Guid SetupId,
    RecordedSessionDerivationWindow? DerivationWindow = null,
    int? VelocityFilterWindowMilliseconds = null);

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
    string RearSuspensionJson);

public sealed record SessionProcessingInputBundle(
    SessionProcessingInput Session,
    SetupProcessingInput Setup,
    BikeProcessingInput Bike,
    RecordedSessionSourceSnapshot Source);
