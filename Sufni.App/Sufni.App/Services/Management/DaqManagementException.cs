using System;

namespace Sufni.App.Services.Management;

public sealed class DaqManagementException : Exception
{
    public DaqManagementException(string message)
        : base(message)
    {
    }

    public DaqManagementException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}