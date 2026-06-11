namespace Sufni.App.ExtensionHost.Contracts.Services;

public interface IExtensionNotificationService
{
    void AddNotification(string message);
    void AddError(string message);
}
