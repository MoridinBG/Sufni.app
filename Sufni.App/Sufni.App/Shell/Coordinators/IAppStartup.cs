using Sufni.App.Shared.Models;
using System;
using System.Threading.Tasks;

namespace Sufni.App.Shell.Coordinators;

public interface IAppStartup
{
    ShellStartupState State { get; }
    string LogPath { get; }
    IObservable<ShellStartupState> Observe();
    Task StartAsync(Action activatePages);
    Task StopAsync();
    Task<DatabaseResetResult> ResetDatabaseAsync();
}

public abstract record ShellStartupState
{
    private ShellStartupState() { }
    public sealed record Starting : ShellStartupState;
    public sealed record Ready : ShellStartupState;
    public sealed record Failed(StartupFailureReason Reason, string Details) : ShellStartupState;
    public sealed record RelaunchRequired(string ArchiveDirectory) : ShellStartupState;
}

public abstract record DatabaseResetResult
{
    private DatabaseResetResult() { }
    public sealed record Reset(string ArchiveDirectory) : DatabaseResetResult;
    public sealed record Failed : DatabaseResetResult;
}
