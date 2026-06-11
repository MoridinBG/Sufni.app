using NSubstitute;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.ExtensionHost.Services;

namespace Sufni.App.ExtensionHost.TestSupport;

// Single construction site for the host services/operations records used by
// recorded-session scope tests.
public static class TestRecordedSessionHostContexts
{
    public static RecordedSessionHostContext CreateContext(
        Guid sessionId,
        IExtensionDatabaseConnection? connection = null,
        Action<double, double>? setAnalysisRange = null,
        Action<string>? addError = null,
        Action<string>? addNotification = null,
        IRecordedSessionDataReader? dataReader = null,
        IBackgroundTaskRunner? backgroundTaskRunner = null,
        IUiThreadDispatcher? uiThreadDispatcher = null,
        Action<double, double, object>? setTimelineVisibleRange = null,
        Func<string, IRecordedSessionOperationLease>? startOperation = null) => new(
        sessionId,
        new RecordedSessionHostServices(
            new EmptyObservable<RecordedSessionHostState>(),
            connection ?? Substitute.For<IExtensionDatabaseConnection>(),
            dataReader ?? Substitute.For<IRecordedSessionDataReader>(),
            backgroundTaskRunner ?? Substitute.For<IBackgroundTaskRunner>(),
            uiThreadDispatcher ?? Substitute.For<IUiThreadDispatcher>()),
        new DelegatingRecordedSessionHostOperations(
            setAnalysisRange,
            setTimelineVisibleRange: setTimelineVisibleRange,
            addError: addError,
            addNotification: addNotification,
            startOperation: startOperation));

    private sealed class EmptyObservable<T> : IObservable<T>
    {
        public IDisposable Subscribe(IObserver<T> observer) => new EmptyDisposable();
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
