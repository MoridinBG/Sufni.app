using System;
using System.Threading.Tasks;

namespace Sufni.App.Services;

public class SynchronizationServerService : ISynchronizationServerService
{
    public static readonly string ServiceType = "_sstsync._tcp";
    public const int PinTtlSeconds = 30;

    public Task StartAsync()
    {
        throw new NotImplementedException();
    }

    public Action<string, string>? PairingPinCallback { get; set; }
}