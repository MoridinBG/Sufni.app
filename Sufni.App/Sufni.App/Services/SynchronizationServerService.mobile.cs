using System;
using System.Threading.Tasks;
using Sufni.App.Models;

namespace Sufni.App.Services;

public class PairingEventArgs(PairedDevice device) : EventArgs
{
    public PairedDevice Device { get; set; } = device;
}

public class SynchronizationServerService : ISynchronizationServerService
{
    public const string ServiceType = "_sstsync._tcp";
    public const string CertificateSubjectName = "cn=com.sghctoma.sst-api";
    public const int PinTtlSeconds = 30;
    public const string EndpointPairRequest = "/pair/request";
    public const string EndpointPairConfirm = "/pair/confirm";
    public const string EndpointPairRefresh = "/pair/refresh";
    public const string EndpointPairUnpair = "/pair/unpair";
    public const string EndpointPairStatus = "/pair/status";
    public const string EndpointSyncPush = "/sync/push";
    public const string EndpointSyncPull = "/sync/pull";
    public const string EndpointSessionIncomplete = "/session/incomplete";
    public const string EndpointSessionData = "/session/data/";

    public Task StartAsync()
    {
        throw new NotImplementedException();
    }
}