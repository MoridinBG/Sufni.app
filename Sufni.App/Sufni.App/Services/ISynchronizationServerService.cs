using System;
using System.Threading.Tasks;

namespace Sufni.App.Services;

public interface ISynchronizationServerService
{
    public Task StartAsync();
    public Action<string, string>? PairingPinCallback { get; set; }
}