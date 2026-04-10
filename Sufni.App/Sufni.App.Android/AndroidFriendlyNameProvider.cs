using Android.OS;
using Sufni.App.Services;

namespace Sufni.App.Android;

public sealed class AndroidFriendlyNameProvider : IFriendlyNameProvider
{
    public string FriendlyName => $"{Build.Manufacturer} {Build.Model}";
}