using System;

namespace Sufni.App.Services.Management;

public sealed class DaqManagementException : Exception
{
    public DaqManagementErrorCode? ErrorCode { get; }

    public ulong? DeclaredFileSizeBytes { get; }

    public ulong? MaximumSupportedFileSizeBytes { get; }

    public DaqManagementException(string message)
        : this(message, null, null, null, null)
    {
    }

    public DaqManagementException(string message, Exception innerException)
        : this(message, innerException, null, null, null)
    {
    }

    public DaqManagementException(DaqManagementErrorCode errorCode, string message)
        : this(message, null, errorCode, null, null)
    {
    }

    public DaqManagementException(
        string message,
        ulong declaredFileSizeBytes,
        ulong maximumSupportedFileSizeBytes)
        : this(message, null, null, declaredFileSizeBytes, maximumSupportedFileSizeBytes)
    {
    }

    private DaqManagementException(
        string message,
        Exception? innerException,
        DaqManagementErrorCode? errorCode,
        ulong? declaredFileSizeBytes,
        ulong? maximumSupportedFileSizeBytes)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        DeclaredFileSizeBytes = declaredFileSizeBytes;
        MaximumSupportedFileSizeBytes = maximumSupportedFileSizeBytes;
    }
}