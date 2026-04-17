namespace Sufni.App.Services.Management;

public enum DaqManagementErrorCode
{
    InvalidRequest = -1,
    NotFound = -2,
    Busy = -3,
    IoError = -4,
    ValidationError = -5,
    UnsupportedTarget = -6,
    InternalError = -7,
}