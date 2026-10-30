using System;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Infrastructure;

namespace Sufni.App;

public sealed record PlatformRoot(
    AppCapabilities Capabilities,
    InputCapabilities Input,
    LaunchPreferences LaunchPreferences,
    Action<IServiceCollection> RegisterPlatformServices,
    IAppPaths Paths);
