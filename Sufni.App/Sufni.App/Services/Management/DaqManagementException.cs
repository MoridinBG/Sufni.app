using System;

namespace Sufni.App.Services.Management;

public sealed class DaqManagementException : Exception
{
    public DaqManagementErrorCode? ErrorCode { get; }

    public DaqManagementException(string message)
        : this(message, null, null)
    {
    }

    public DaqManagementException(string message, Exception innerException)
        : this(message, innerException, null)
    {
    }

    public DaqManagementException(DaqManagementErrorCode errorCode, string message)
        : this(message, null, errorCode)
    {
    }

    private DaqManagementException(
        string message,
        Exception? innerException,
        DaqManagementErrorCode? errorCode)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}