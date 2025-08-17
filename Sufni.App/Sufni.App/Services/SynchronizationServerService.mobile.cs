using System;
using System.Threading.Tasks;
using Sufni.App.Models;

namespace Sufni.App.Services;

public class SynchronizationServerService : ISynchronizationServerService
{
    public static readonly string ServiceType = "_sstsync._tcp";
    public static readonly string CertificateSubjectName = "cn=com.sghctoma.sst-api";
    public const int PinTtlSeconds = 30;

    public Task StartAsync()
    {
        throw new NotImplementedException();
    }

    public Action<string, string>? PairingRequested { get; set; }
    public Action? PairingConfirmed { get; set; }
    public Action<SynchronizationData>? SynchronizationDataArrived { get; set; }
    public Action<Guid>? SessionDataArrived { get; set; }
}