namespace Sufni.App.Coordinators;

/// <summary>
/// Desktop-only marker interface. The coordinator has no public
/// surface — its only job is to subscribe to the synchronization
/// server's <c>SynchronizationDataArrived</c> event in its constructor
/// and write incoming bikes / setups into their respective stores.
/// The interface exists only so the DI container can register and
/// eagerly resolve the singleton at startup.
/// </summary>
public interface IInboundSyncCoordinator
{
}
