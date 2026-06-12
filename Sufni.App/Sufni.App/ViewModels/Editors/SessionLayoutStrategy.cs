using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Coordinators;
using Sufni.App.SessionDetails;

namespace Sufni.App.ViewModels.Editors;

/// <summary>
/// Shell-shaped recorded-session behavior, selected at construction by
/// <c>EditorFactory</c>: which load pipeline a session detail uses and
/// whether domain changes are deferred while the tab is inactive.
/// </summary>
internal interface ISessionLayoutStrategy
{
    bool DefersDomainHandlingWhenInactive { get; }

    Task LoadDetailAsync(
        ISessionCoordinator sessionCoordinator,
        Guid sessionId,
        SessionPresentationDimensions? presentationDimensions,
        RecordedPresentationApplier applier,
        CancellationToken token);
}

internal sealed class DesktopSessionLayoutStrategy : ISessionLayoutStrategy
{
    public bool DefersDomainHandlingWhenInactive => true;

    public async Task LoadDetailAsync(
        ISessionCoordinator sessionCoordinator,
        Guid sessionId,
        SessionPresentationDimensions? presentationDimensions,
        RecordedPresentationApplier applier,
        CancellationToken token)
    {
        var result = await sessionCoordinator.LoadDesktopDetailAsync(sessionId, token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        applier.ApplyDesktopLoadResult(result);
    }
}

internal sealed class MobileSessionLayoutStrategy : ISessionLayoutStrategy
{
    public bool DefersDomainHandlingWhenInactive => false;

    public async Task LoadDetailAsync(
        ISessionCoordinator sessionCoordinator,
        Guid sessionId,
        SessionPresentationDimensions? presentationDimensions,
        RecordedPresentationApplier applier,
        CancellationToken token)
    {
        if (presentationDimensions is null)
        {
            return;
        }

        var result = await sessionCoordinator.LoadMobileDetailAsync(sessionId, presentationDimensions.Value, token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        applier.ApplyMobileLoadResult(result);
    }
}
