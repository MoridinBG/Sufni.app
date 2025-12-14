using Android.OS;

namespace FriendlyNameProvider;

public class FriendlyNameProvider : IFriendlyNameProvider
{
    public string FriendlyName => $"{Build.Manufacturer} {Build.Model}";
}
