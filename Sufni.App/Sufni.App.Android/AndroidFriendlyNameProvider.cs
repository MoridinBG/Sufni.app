using Android.OS;
using Sufni.App.Infrastructure;

namespace Sufni.App.Android;

public sealed class AndroidFriendlyNameProvider : IFriendlyNameProvider
{
    public string FriendlyName => $"{Build.Manufacturer} {Build.Model}";
}