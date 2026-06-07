namespace Sufni.App.Services;

public interface IExtensionNotificationService
{
    void AddNotification(string message);
    void AddError(string message);
}
