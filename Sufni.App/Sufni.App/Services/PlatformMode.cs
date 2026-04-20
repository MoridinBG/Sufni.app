namespace Sufni.App.Services;

public sealed class PlatformMode(bool isDesktop) : IPlatformMode
{
    public bool IsDesktop { get; } = isDesktop;
}