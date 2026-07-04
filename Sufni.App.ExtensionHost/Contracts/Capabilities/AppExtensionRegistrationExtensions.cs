using System;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sufni.App.ExtensionHost.Contracts.Capabilities;

public static class AppExtensionRegistrationExtensions
{
    public static IServiceCollection AddExtensionSingletonAlias<TService, TImplementation>(
        this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<TImplementation>();
        services.AddSingleton<TService>(sp => sp.GetRequiredService<TImplementation>());
        return services;
    }

    public static void RegisterView<TViewModel, TSharedView>(
        this IAppExtensionCapabilityRegistry registry)
        where TSharedView : Control, new()
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.RegisterView(
            typeof(TViewModel),
            static () => new TSharedView());
    }

    public static void RegisterView<TViewModel>(
        this IAppExtensionCapabilityRegistry registry,
        Func<IServiceProvider, Control> sharedFactory,
        Func<IServiceProvider, Control>? compactFactory = null,
        Func<IServiceProvider, Control>? workspaceFactory = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(sharedFactory);

        registry.RegisterView(
            typeof(TViewModel),
            sharedFactory,
            compactFactory,
            workspaceFactory);
    }

    public static void RegisterView<TViewModel, TSharedView, TWorkspaceView>(
        this IAppExtensionCapabilityRegistry registry)
        where TSharedView : Control, new()
        where TWorkspaceView : Control, new()
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.RegisterView(
            typeof(TViewModel),
            static () => new TSharedView(),
            workspaceFactory: static () => new TWorkspaceView());
    }

    public static void RegisterView<TViewModel, TSharedView, TCompactView, TWorkspaceView>(
        this IAppExtensionCapabilityRegistry registry)
        where TSharedView : Control, new()
        where TCompactView : Control, new()
        where TWorkspaceView : Control, new()
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.RegisterView(
            typeof(TViewModel),
            static () => new TSharedView(),
            compactFactory: static () => new TCompactView(),
            workspaceFactory: static () => new TWorkspaceView());
    }
}
