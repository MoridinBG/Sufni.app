namespace Sufni.App.ExtensionHost.RecordedSessions;

public interface IRecordedSessionExtensionFactory
{
    string ExtensionId { get; }
    IRecordedSessionExtensionScope Create(RecordedSessionHostContext context);
}
