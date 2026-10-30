using System;
using Avalonia.Threading;
using Serilog;
using Sufni.App.Shell.ViewModels;

namespace Sufni.App.Shell.Behaviors;

public sealed class ShellDispatcherExceptionHandler : IDisposable
{
    private readonly ShellFeedbackViewModel feedback;

    public ShellDispatcherExceptionHandler(ShellFeedbackViewModel feedback)
    {
        this.feedback = feedback;
        Dispatcher.UIThread.UnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        Log.Error(args.Exception, "Unhandled dispatcher operation failed");
        feedback.ShowUnexpectedFailure();
        args.Handled = true;
    }

    public void Dispose() => Dispatcher.UIThread.UnhandledException -= OnUnhandledException;
}
