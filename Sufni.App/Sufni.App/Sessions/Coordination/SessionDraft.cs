using System;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;

namespace Sufni.App.Sessions.Coordination;

/// <summary>The authored fields saved together by the recorded-session editor.</summary>
public sealed record SessionDraft(
    Guid Id,
    string Name,
    string Description,
    Guid? SetupId,
    string? FrontSpringRate,
    uint? FrontHighSpeedCompression,
    uint? FrontLowSpeedCompression,
    uint? FrontLowSpeedRebound,
    uint? FrontHighSpeedRebound,
    string? RearSpringRate,
    uint? RearHighSpeedCompression,
    uint? RearLowSpeedCompression,
    uint? RearLowSpeedRebound,
    uint? RearHighSpeedRebound)
{
    public static SessionDraft From(SessionSnapshot snapshot) => new(
        snapshot.Id,
        snapshot.Name,
        snapshot.Description,
        snapshot.SetupId,
        snapshot.FrontSpringRate,
        snapshot.FrontHighSpeedCompression,
        snapshot.FrontLowSpeedCompression,
        snapshot.FrontLowSpeedRebound,
        snapshot.FrontHighSpeedRebound,
        snapshot.RearSpringRate,
        snapshot.RearHighSpeedCompression,
        snapshot.RearLowSpeedCompression,
        snapshot.RearLowSpeedRebound,
        snapshot.RearHighSpeedRebound);

    public Session ApplyTo(SessionSnapshot snapshot)
    {
        var session = snapshot.ToMetadataEntity();
        session.Name = Name;
        session.Description = Description;
        session.Setup = SetupId;
        session.FrontSpringRate = FrontSpringRate;
        session.FrontHighSpeedCompression = FrontHighSpeedCompression;
        session.FrontLowSpeedCompression = FrontLowSpeedCompression;
        session.FrontLowSpeedRebound = FrontLowSpeedRebound;
        session.FrontHighSpeedRebound = FrontHighSpeedRebound;
        session.RearSpringRate = RearSpringRate;
        session.RearHighSpeedCompression = RearHighSpeedCompression;
        session.RearLowSpeedCompression = RearLowSpeedCompression;
        session.RearLowSpeedRebound = RearLowSpeedRebound;
        session.RearHighSpeedRebound = RearHighSpeedRebound;
        return session;
    }
}
