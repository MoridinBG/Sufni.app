using Sufni.App.Infrastructure;
using UIKit;

namespace Sufni.App.iOS;

public sealed class IosFriendlyNameProvider : IFriendlyNameProvider
{
    public string FriendlyName => UIDevice.CurrentDevice.Name;
}