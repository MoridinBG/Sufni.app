using System;

namespace Sufni.App.Shared.Common;

internal sealed class EmptyServiceProvider : IServiceProvider
{
    public static EmptyServiceProvider Instance { get; } = new();

    public object? GetService(Type serviceType) => null;
}
