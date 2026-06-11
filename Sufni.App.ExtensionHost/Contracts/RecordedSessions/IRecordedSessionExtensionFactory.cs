namespace Sufni.App.ExtensionHost.Contracts.RecordedSessions;

public interface IRecordedSessionExtensionFactory
{
    string ExtensionId { get; }
    IRecordedSessionExtensionScope Create(RecordedSessionHostContext context);
}
