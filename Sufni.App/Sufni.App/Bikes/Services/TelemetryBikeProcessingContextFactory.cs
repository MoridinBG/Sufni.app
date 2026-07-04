using System;
using Sufni.Telemetry;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure.Caching;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Setups.Stores;

namespace Sufni.App.Bikes.Services;

internal interface ITelemetryBikeProcessingContextFactory
{
    TelemetryBikeProcessingContext Create(SetupSnapshot setup, BikeSnapshot bike);
}

internal sealed record TelemetryBikeProcessingContext(
    BikeData BikeData,
    ISensorConfiguration? FrontSensorConfiguration,
    RearTravelCalibrationBuildResult RearTravelCalibration);

internal sealed class TelemetryBikeProcessingContextFactory(
    IRearTravelCalibrationBuilder rearTravelCalibrationBuilder) : ITelemetryBikeProcessingContextFactory
{
    private const int Capacity = 16;

    private readonly SingleFlightLruCache<CacheRequest, TelemetryBikeProcessingContext> cache =
        new(Capacity, request => CreateUncached(
            rearTravelCalibrationBuilder,
            request.Setup,
            request.Bike));

    public TelemetryBikeProcessingContext Create(SetupSnapshot setup, BikeSnapshot bike)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(bike);

        var key = ProcessingDependencyInputs.Create(setup, bike);
        return cache.GetOrAdd(new CacheRequest(key, setup, bike with { ImageBytes = [] }));
    }

    private static TelemetryBikeProcessingContext CreateUncached(
        IRearTravelCalibrationBuilder rearTravelCalibrationBuilder,
        SetupSnapshot setup,
        BikeSnapshot bike)
    {
        var frontSensorConfiguration = setup.FrontSensorConfigurationJson is null
            ? null
            : SensorConfiguration.FromJson(setup.FrontSensorConfigurationJson, bike);
        var rearTravelCalibration = rearTravelCalibrationBuilder.TryBuild(setup, bike);

        return new TelemetryBikeProcessingContext(
            TelemetryBikeData.Create(frontSensorConfiguration, rearTravelCalibration.Calibration),
            frontSensorConfiguration,
            rearTravelCalibration);
    }

    private readonly record struct CacheRequest(
        ProcessingDependencyInputs Key,
        SetupSnapshot Setup,
        BikeSnapshot Bike)
    {
        public bool Equals(CacheRequest other) => Key.Equals(other.Key);

        public override int GetHashCode() => Key.GetHashCode();
    }
}
