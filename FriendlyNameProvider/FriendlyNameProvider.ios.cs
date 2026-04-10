using UIKit;

namespace FriendlyNameProvider;

public class FriendlyNameProvider : IFriendlyNameProvider
{
    public string FriendlyName => UIDevice.CurrentDevice.Name;
}

