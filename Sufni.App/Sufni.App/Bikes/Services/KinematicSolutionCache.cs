using System;
using Sufni.Kinematics;
using Serilog;
using Sufni.App.Infrastructure.Caching;

namespace Sufni.App.Bikes.Services;

internal interface IKinematicSolutionCache
{
    KinematicSolution GetOrSolve(LinkageSpec linkage, int steps = 200, int iterations = 1000);
}

internal readonly record struct KinematicSolutionKey(
    LinkageSpec Linkage,
    int Steps,
    int Iterations);

internal sealed class KinematicSolutionCache : IKinematicSolutionCache
{
    private const int Capacity = 64;
    private static readonly ILogger logger = Log.ForContext<KinematicSolutionCache>();

    private readonly SingleFlightLruCache<KinematicSolutionKey, KinematicSolution> cache;
    private readonly Func<LinkageSpec, int, int, KinematicSolution> solve;

    public KinematicSolutionCache()
        : this(static (linkage, steps, iterations) => new KinematicSolver(linkage, steps, iterations).SolveSuspensionMotion())
    {
    }

    internal KinematicSolutionCache(Func<LinkageSpec, int, int, KinematicSolution> solve)
    {
        this.solve = solve ?? throw new ArgumentNullException(nameof(solve));
        cache = new SingleFlightLruCache<KinematicSolutionKey, KinematicSolution>(Capacity, Solve);
    }

    public KinematicSolution GetOrSolve(LinkageSpec linkage, int steps = 200, int iterations = 1000)
    {
        ArgumentNullException.ThrowIfNull(linkage);

        return cache.GetOrAdd(new KinematicSolutionKey(linkage, steps, iterations));
    }

    private KinematicSolution Solve(KinematicSolutionKey key)
    {
        logger.Debug(
            "Solving kinematic solution cache miss with {StepCount} steps and {IterationCount} iterations",
            key.Steps,
            key.Iterations);

        try
        {
            return solve(key.Linkage, key.Steps, key.Iterations);
        }
        catch (Exception exception)
        {
            logger.Warning(
                exception,
                "Kinematic solution solve failed with {StepCount} steps and {IterationCount} iterations",
                key.Steps,
                key.Iterations);
            throw;
        }
    }
}
