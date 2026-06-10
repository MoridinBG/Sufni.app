namespace Sufni.App.ExtensionHost.Services;

public interface IExtensionNotificationService
{
    void AddNotification(string message);
    void AddError(string message);
}
