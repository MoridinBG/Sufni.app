using System;

namespace Sufni.App.Services;

public enum SynchronizationPhase
{
    ResolvingServer,
    PushingLocalChanges,
    PullingRemoteChanges,
    PushingIncompleteSessions,
    PullingIncompleteSessions,
    PushingIncompleteSessionSources,
    PullingIncompleteSessionSources,
    RefreshingLocalStores,
    ReceivingChanges,
    ServingChanges,
    CheckingIncompleteSessions,
    ReceivingSessionData,
    ServingSessionData,
    CheckingIncompleteSessionSources,
    ReceivingSessionSourceData,
    ServingSessionSourceData
}

public sealed record SynchronizationProgressSnapshot(
    SynchronizationPhase Phase,
    string Message,
    int CurrentStep,
    int TotalSteps,
    bool IsDeterminate)
{
    public double Fraction =>
        IsDeterminate && TotalSteps > 0
            ? Math.Clamp((double)CurrentStep / TotalSteps, 0, 1)
            : 0;
}

public sealed record SynchronizationActivityEventArgs(SynchronizationProgressSnapshot Progress);
