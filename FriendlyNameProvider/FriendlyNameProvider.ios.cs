using UIKit;

namespace FriendlyName;

public class FriendlyNameProvider : IFriendlyNameProvider
{
    public string FriendlyName => UIDevice.CurrentDevice.Name;
}

